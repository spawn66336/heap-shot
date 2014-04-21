using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace HeapShot.Reader
{
    public class HeapShotUpdateManager
    {
        private static HeapShotUpdateManager g_updateMgr;

        static HeapShotUpdateManager()
        {
            g_updateMgr = new HeapShotUpdateManager();
        }

        public static HeapShotUpdateManager Instance
        {
            get { return g_updateMgr; }
        }
        private HeapShotUpdateManager()
        {
        }


        public MonoProfilerReaderBridge.HeapShot heapShotRef
        {
             set 
             { 
                heapShot = value;

                if( heapShot == null )
                {
                    fileSysWatcher = null;
                    return;
                }

                heapDataCount = heapShot.GetHeapDataCount();
                classCount = heapShot.GetClassInfoCount();

                string filePath = heapShot.GetFilePath();
                //提取文件所在路径
                string fileDir = filePath.Substring(0, filePath.LastIndexOf('\\') + 1);
                string fileName = filePath.Substring(filePath.LastIndexOf('\\') + 1);

                Console.WriteLine("正在监听文件\"{0}\"...",filePath);

                //只监听当前HeapShot所对应文件
                fileSysWatcher = new FileSystemWatcher(fileDir,fileName);
                fileSysWatcher.NotifyFilter = NotifyFilters.LastWrite;
                fileSysWatcher.Changed += this.OnFileChanged;
                fileSysWatcher.EnableRaisingEvents = true;
                
             } 
        }

        public void Clear()
        { 
            shots.Clear();
            heapShotRef = null;
        }

        void OnFileChanged(object sender, FileSystemEventArgs e)
        {

            Console.WriteLine("{0}.{1}: 监测到文件变更...", DateTime.Now.ToShortTimeString() , DateTime.Now.Second);
            
            heapShot.Update();

            if( heapShot.GetHeapDataCount() > heapDataCount )
            {
                Console.WriteLine("新增{0}个截面", heapShot.GetHeapDataCount() - heapDataCount);

                //增加截面
                for( uint i = heapDataCount ; i < heapShot.GetHeapDataCount(); i++ )
                {
                    HeapSnapshot newShot = new HeapSnapshot();
                    newShot.name = (i+1).ToString();
                    newShot.heapShot = heapShot;
                    newShot.heapDataId = (int)i; 
                    
                    AppendHeapSnapShot(newShot);
                    //增加截面
                    if (HeapSnapshotAdded != null)
                        HeapSnapshotAdded(this, new HeapShotEventArgs(newShot));
                }

                heapDataCount = heapShot.GetHeapDataCount();
            }

            //类有更新,通知所有HeapSnapshot需要重建
            if( heapShot.GetClassInfoCount() != classCount )
            {
                for(int i = 0 ; i < shots.Count ; i++ )
                {
                    shots[i].IsBuild = false;
                }
            }

            //更新类数量
            classCount = heapShot.GetClassInfoCount();
        }

        public void AppendHeapSnapShot( HeapSnapshot newShot )
        {
            shots.Add(newShot);
        }

        MonoProfilerReaderBridge.HeapShot heapShot;
        //当前HeapShot文件中的截面数量
        uint heapDataCount;
        //当前截面类数量（用于侦测类是否有变化）
        uint classCount;

        //HeapSnapShot列表用于通知重建
        List<HeapSnapshot> shots = new List<HeapSnapshot>();

        FileSystemWatcher fileSysWatcher;

        //增加截面用的回调
        public event EventHandler<HeapShotEventArgs> HeapSnapshotAdded;
    }
}
