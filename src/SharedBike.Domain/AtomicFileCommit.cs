using System;
using System.IO;

namespace DuckovSharedBike.Domain
{
    public interface IAtomicFileOperations
    {
        bool Exists(string path);
        void Flush(string temporaryPath);
        void Replace(string temporaryPath,string destination,string backup);
        void Move(string temporaryPath,string destination);
    }

    public sealed class SystemAtomicFileOperations : IAtomicFileOperations
    {
        public bool Exists(string path)=>File.Exists(path);
        public void Flush(string temporaryPath)
        {
            using (var stream=new FileStream(temporaryPath,FileMode.Open,FileAccess.ReadWrite,FileShare.Read)) stream.Flush(true);
        }
        public void Replace(string temporaryPath,string destination,string backup)=>File.Replace(temporaryPath,destination,backup,true);
        public void Move(string temporaryPath,string destination)=>File.Move(temporaryPath,destination);
    }

    public static class AtomicFileCommit
    {
        public static void Commit(string destination,IAtomicFileOperations operations)
        {
            if (string.IsNullOrWhiteSpace(destination) || operations==null) throw new ArgumentException("Invalid atomic save target.");
            var path=Path.GetFullPath(destination);
            var temporary=path+".tmp";
            if (!operations.Exists(temporary)) throw new IOException("Native temporary save is missing.");
            operations.Flush(temporary);
            // Do not delete the live file. On failure it remains the previous complete checkpoint.
            if (operations.Exists(path)) operations.Replace(temporary,path,path+".sharedbike.bak");
            else operations.Move(temporary,path);
        }
    }
}
