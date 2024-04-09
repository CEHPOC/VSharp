using System.Diagnostics;

namespace VSharp.SAST;

public class SecurityCodeScan
{
    public string workDir;
    public string sln;
    private ProcessStartInfo proc;

    public SecurityCodeScan(string slnPath, string workPath)
    {
        sln = slnPath;
        workDir = workPath;
        proc = new ProcessStartInfo()
        {
            UseShellExecute = true,
            WorkingDirectory = workDir,
            FileName = @"C:\Windows\System32\cmd.exe",
            Arguments = "/c security-scan " + sln + " --export=sast-results.sarif",
            WindowStyle = ProcessWindowStyle.Hidden
        };
    }

    public void run()
    {
        Process.Start(proc);
    }
}