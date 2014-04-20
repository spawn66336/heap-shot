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
            heapShotRef = null;
        }

        void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine("文件名:" + e.Name + "\n变更类型:" + e.GetType());
            
            heapShot.Update();

            if( heapShot.GetHeapDataCount() > heapDataCount )
            {
                Console.WriteLine("新增{0}个截面。", heapShot.GetHeapDataCount() - heapDataCount);

                //增加截面
                for( uint i = heapDataCount ; i < heapShot.GetHeapDataCount(); i++ )
                {
                    HeapSnapshot newShot = new HeapSnapshot();
                    newShot.name = (i+1).ToString();
                    newShot.heapShot = heapShot;
                    newShot.heapDataId = (int)i;

                    //增加截面
                    if (HeapSnapshotAdded != null)
                        HeapSnapshotAdded(this, new HeapShotEventArgs(newShot));
                }

                heapDataCount = heapShot.GetHeapDataCount();
            }
        }

        MonoProfilerReaderBridge.HeapShot heapShot;
        //当前HeapShot文件中的截面数量
        uint heapDataCount;  
        FileSystemWatcher fileSysWatcher;

        //增加截面用的回调
        public event EventHandler<HeapShotEventArgs> HeapSnapshotAdded;
    }
}
