//
// ObjectMapFileReader.cs
//
// Copyright (C) 2005 Novell, Inc.
// Copyright (C) 2011 Xamarin Inc. (http://www.xamarin.com)
//
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of version 2 of the GNU General Public
// License as published by the Free Software Foundation.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307
// USA.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using MonoDevelop.Profiler;
using HeapShot.Reader.Graphs;
using System.Net.Sockets;

namespace HeapShot.Reader {

	public class ObjectMapReader: IDisposable
	{
		const string log_file_label = "heap-shot logfile";
		
		string name;
		LogFileReader reader;
		DateTime timestamp;
		ulong totalMemory;
		
		Header header;
		
		long currentPtrBase;
		long currentObjBase;
		
		internal const long UnknownTypeId = -1;
		
		internal const long UnknownObjectId = -1;
		internal const long StackObjectId = -2;
		internal const long FinalizerObjectId = -3;
		internal const long HandleObjectId = -4;
		internal const long OtherRootObjectId = -5;
		internal const long MiscRootObjectId = -6;

		long rootId = StackObjectId - 1;
		
		HeapShotData currentData;
		List<HeapSnapshot> shots = new List<HeapSnapshot> ();
		
		public event EventHandler<HeapShotEventArgs> HeapSnapshotAdded;
		
		internal ObjectMapReader ()
		{
		}
		
		public ObjectMapReader (string filename)
		{
			this.name = filename;
			
			currentData = new HeapShotData ();
			
			// Some stock types
			currentData.TypesList.Add (new TypeInfo () { Code = UnknownObjectId, Name = "<Unknown>" });   // 0
			currentData.TypesList.Add (new TypeInfo () { Code = StackObjectId, Name = "<Stack>" });     // 1
			currentData.TypesList.Add (new TypeInfo () { Code = FinalizerObjectId, Name = "<Finalizer>" }); // 2
			currentData.TypesList.Add (new TypeInfo () { Code = HandleObjectId, Name = "<Handle>" });    // 3
			currentData.TypesList.Add (new TypeInfo () { Code = OtherRootObjectId, Name = "<Other Root>" });      // 4
			currentData.TypesList.Add (new TypeInfo () { Code = MiscRootObjectId, Name = "<Misc Root>" }); // 5
		}
		
		public void Dispose ()
		{ 
		}
		
		
		public void Read ()
		{
			Read (null);
		}
		
		public void Read (IProgressListener progress)
		{
			Stopwatch watch = new Stopwatch ();
			watch.Start ();
			
			try {
				if (!File.Exists (name))
					return;
				 
				ReadLogFile (progress);
			} catch (Exception ex) {
				Console.WriteLine (ex);
			} finally {
				watch.Stop ();
				Console.WriteLine ("ObjectMapFileReader.Read (): Completed in {0} s", watch.ElapsedMilliseconds / (double) 1000);
			}
		}
		
		public bool WaitForHeapShot (int timeout)
		{
			int ns = shots.Count;
			DateTime tlimit = DateTime.Now + TimeSpan.FromMilliseconds (timeout);
			while (DateTime.Now < tlimit) {
				Read ();
				if (shots.Count > ns)
					return true;
				System.Threading.Thread.Sleep (500);
			}
			return false;
		}
		
		public string Name {
			get { return name; }
		}
		
		public DateTime Timestamp {
			get { return timestamp; }
		}
		
		public IList<HeapSnapshot> HeapShots {
			get { return shots; }
		}
		
		public int Port {
			get { return header == null ? 0 : header.Port; }
		}
		
		public void ForceSnapshot ()
		{
			if (header == null) {
				Read ();
				if (header == null)
					throw new Exception ("Log file could not be opened");
			}
			using (TcpClient client = new TcpClient ()) {
				client.Connect ("127.0.0.1", header.Port);
				using (StreamWriter sw = new StreamWriter (client.GetStream ())) {
					sw.WriteLine ("heapshot");
					sw.Flush ();
				}
			}
		}

		//
		// Code to read the log files generated at runtime
		//
		private void ReadLogFile (IProgressListener progress)
		{

            MonoProfilerReaderBridge.ProfilerHeapShotManager mgr = new MonoProfilerReaderBridge.ProfilerHeapShotManager();

            progress.ReportProgress("正在加载..." + this.name, 0.0);
            MonoProfilerReaderBridge.HeapShot heapShot =  mgr.CreateHeapShotFromFile(this.name);
           
            //用来集结类信息的查找表
            Hashtable classMap = new Hashtable(); 
            for (uint i = 0; i < heapShot.GetHeapDataCount(); i++)
            {
                MonoProfilerReaderBridge.HeapData heapData = heapShot.GetHeapDataByIndex(i);

                //对每个HeapData的Object进行遍历
                MonoProfilerReaderBridge.ObjectInfo obj = null;
                heapData.MoveFirstObject();
                while ((obj = heapData.GetCurrObject()) != null)
                {
                    if (!classMap.ContainsKey(obj.GetClassID()))
                    {
                        classMap[obj.GetClassID()] = heapShot.GetClassInfoByID(obj.GetClassID());
                    }
                    heapData.MoveNextObject();
                }
            }

            //将类信息装进HeapShotData中
            List<TypeInfo> typeList = new List<TypeInfo>();
            IDictionaryEnumerator classEnum = classMap.GetEnumerator();
            classEnum.Reset();
            while (classEnum.MoveNext())
            {
                DictionaryEntry dentry;
                dentry = (DictionaryEntry)classEnum.Current;

                MonoProfilerReaderBridge.ClassInfo classInfo = (MonoProfilerReaderBridge.ClassInfo)dentry.Value;
                TypeInfo ti = new TypeInfo();
                ti.Code = classInfo.GetID();
                ti.Name = classInfo.GetName();
                ti.FieldsIndex = currentData.FieldCodes.Count;
                ti.FieldsCount = 0;
                typeList.Add(ti);
            }



            for (uint i = 0; i < heapShot.GetHeapDataCount(); i++ )
            {
                MonoProfilerReaderBridge.HeapData heapData = heapShot.GetHeapDataByIndex(i);

                //对每个HeapData的Object进行遍历
                MonoProfilerReaderBridge.ObjectInfo obj = null;
                heapData.MoveFirstObject();
                while( ( obj = heapData.GetCurrObject() ) != null )
                {
                    if( !classMap.ContainsKey( obj.GetClassID() ) )
                    {
                        classMap[obj.GetClassID()] = heapShot.GetClassInfoByID(obj.GetClassID());
                    }

                    ObjectInfo ob = new ObjectInfo(); 
                    ob.Code = obj.GetID();
                    ob.Size = obj.GetSize();
                    ob.RefsIndex = currentData.ReferenceCodes.Count;
                    ob.RefsCount = (int)obj.GetRefObjCount();
                    currentData.ObjectTypeCodes.Add( obj.GetClassID());
                    totalMemory += ob.Size;
                    if (ob.Size != 0)
                        currentData.RealObjectCount++;

                    // Read referenceCodes 
                    for (uint n = 0; n < ob.RefsCount; n++)
                    {
                        currentData.ReferenceCodes.Add((long)obj.GetRefObjIDByIndex(n)); 
                    }
                    currentData.ObjectsList.Add(ob);

                    heapData.MoveNextObject(); 
                }

                currentData.TypesList.Clear();
                currentData.TypesList.AddRange(typeList);

                HeapSnapshot shot = new HeapSnapshot();
                shotCount++;
                shot.name = shotCount.ToString();
                //若当前HeapData已经载入
                if (heapData.IsLoaded())
                {
                    shot.Build(shotCount.ToString(), currentData);
                }
                AddShot(shot);

                currentData.ResetHeapData();

                double prog = ((double)i) / ((double)heapShot.GetHeapDataCount());
                progress.ReportProgress("构建堆快照...", prog);
            }//for (uint i = 0; i < heapShot.GetHeapDataCount(); i++ )


                mgr = null; 
		}

		private void ReadLogFileChunk_Type (MetadataEvent t)
		{
			if (t.MType != MetadataEvent.MetaDataType.Class)
				return;
			
			TypeInfo ti = new TypeInfo ();
			ti.Code = t.Pointer + currentPtrBase;
			ti.Name = t.Name;
			ti.FieldsIndex = currentData.FieldCodes.Count;
			
			int nf = 0;
/*			uint fcode;
			while ((fcode = reader.ReadUInt32 ()) != 0) {
				fieldCodes.Add (fcode);
				fieldNamesList.Add (reader.ReadString ());
				nf++;
			}*/
			ti.FieldsCount = nf;
			currentData.TypesList.Add (ti);
		}
		
		void ReadGcEvent (GcEvent ge)
		{
			if (ge.EventType == GcEvent.GcEventType.Start)
				currentData.ResetHeapData ();
		}
		
		int shotCount;
		
		private void ReadLogFileChunk_Object (HeapEvent he)
		{
			if (he.Type == HeapEvent.EventType.Start) {
				//Console.WriteLine ("ppe: START");
				return;
			}
			else if (he.Type == HeapEvent.EventType.End) {
				//Console.WriteLine ("ppe: END");
				HeapSnapshot shot = new HeapSnapshot ();
				shotCount++;
				shot.Build (shotCount.ToString (), currentData);
				AddShot (shot);
			}
			if (he.Type == HeapEvent.EventType.Object) {
				ObjectInfo ob = new ObjectInfo ();
				ob.Code = currentObjBase + he.Object;
				ob.Size = he.Size;
				ob.RefsIndex = currentData.ReferenceCodes.Count;
				ob.RefsCount = he.ObjectRefs != null ? he.ObjectRefs.Length : 0;
				currentData.ObjectTypeCodes.Add (currentPtrBase + he.Class);
				totalMemory += ob.Size;
				if (ob.Size != 0)
					currentData.RealObjectCount++;
				
				// Read referenceCodes
				
				ulong lastOff = 0;
				for (int n=0; n < ob.RefsCount; n++) {
					currentData.ReferenceCodes.Add (he.ObjectRefs [n] + currentObjBase);
					lastOff += he.RelOffset [n];
					currentData.FieldReferenceCodes.Add (lastOff);
				}
				currentData.ObjectsList.Add (ob);
			}
			else if (he.Type == HeapEvent.EventType.Root) {
				for (int n=0; n<he.RootRefs.Length; n++) {
					ObjectInfo ob = new ObjectInfo ();
					ob.Size = 0;
					ob.RefsIndex = currentData.ReferenceCodes.Count;
					ob.RefsCount = 1;
					long type = UnknownTypeId;
					switch (he.RootRefTypes [n] & HeapEvent.RootType.TypeMask) {
					case HeapEvent.RootType.Stack: type = StackObjectId; ob.Code = StackObjectId; break;
					case HeapEvent.RootType.Finalizer: type = FinalizerObjectId; ob.Code = --rootId; break;
					case HeapEvent.RootType.Handle: type = HandleObjectId; ob.Code = --rootId; break;
					case HeapEvent.RootType.Other: type = OtherRootObjectId; ob.Code = --rootId; break;
					case HeapEvent.RootType.Misc: type = MiscRootObjectId; ob.Code = --rootId; break;
					default:
						Console.WriteLine ("pp1:"); break;
					}
					currentData.ObjectTypeCodes.Add (type);
					currentData.ReferenceCodes.Add (he.RootRefs [n] + currentObjBase);
					currentData.FieldReferenceCodes.Add (0);
					currentData.ObjectsList.Add (ob);
					currentData.RealObjectCount++;
				}
			}
		}
		
		void AddShot (HeapSnapshot shot)
		{
			shots.Add (shot);
			if (HeapSnapshotAdded != null)
				HeapSnapshotAdded (this, new HeapShotEventArgs (shot));
		}
	}
	
	internal class HeapShotData
	{
		public HeapShotData ()
		{
			ObjectsList = new List<ObjectInfo> ();
			TypesList = new List<TypeInfo> ();
			ObjectTypeCodes = new List<long> ();
			ReferenceCodes = new List<long> ();
			FieldReferenceCodes = new List<ulong> ();
			FieldCodes = new List<uint> ();
			FieldNamesList = new List<string> ();
		}
		
		public void ResetHeapData ()
		{
			ObjectsList.Clear ();
			ObjectTypeCodes.Clear ();
			ReferenceCodes.Clear ();
			FieldReferenceCodes.Clear ();
			RealObjectCount = 1;
			
			// The 'unknown' object
			ObjectInfo ob = new ObjectInfo ();
			ob.Code = ObjectMapReader.UnknownObjectId;
			ob.Size = 0;
			ob.RefsIndex = 0;
			ob.RefsCount = 0;
			ObjectTypeCodes.Add (ObjectMapReader.UnknownTypeId);
			ObjectsList.Add (ob);
		}
		
		public int RealObjectCount;
		public List<ObjectInfo> ObjectsList;
		public List<TypeInfo> TypesList;
		public List<string> FieldNamesList;
		public List<long> ReferenceCodes;
		public List<long> ObjectTypeCodes;
		public List<uint> FieldCodes;
		public List<ulong> FieldReferenceCodes;
	}
}
