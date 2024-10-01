using System.Diagnostics;

namespace VSharp.SAST;

public class PublishRunner
{
    public string workDir;
    private ProcessStartInfo proc;

    public PublishRunner(string workPath)
    {
        workDir = workPath;
        proc = new ProcessStartInfo()
        {
            UseShellExecute = true,
            WorkingDirectory = workDir,
            FileName = @"C:\Windows\System32\cmd.exe",
            Arguments = "/c dotnet publish --self-contained -r win-x64 -c Release", //только win-x64, нужно подумать
            //WindowStyle = ProcessWindowStyle.Hidden
        };
    }

    public void run()
    {
        Process cmd = Process.Start(proc);
        cmd.WaitForExit();
    }
}