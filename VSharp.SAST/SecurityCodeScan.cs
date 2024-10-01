using System.Diagnostics;

namespace VSharp.SAST;

public class SecurityCodeScan
{
    public string workDir;
    public string sln;
    private ProcessStartInfo proc1;
    private ProcessStartInfo proc2;

    public SecurityCodeScan(string slnPath, string workPath)
    {
        sln = slnPath;
        workDir = workPath;
        proc1 = new ProcessStartInfo()
        {
            UseShellExecute = true,
            WorkingDirectory = workDir,
            FileName = @"C:\Windows\System32\cmd.exe",
            Arguments = "/c codeql database create codeql-dbs --language=csharp",
            //WindowStyle = ProcessWindowStyle.Hidden
        };
        proc2 = new ProcessStartInfo()
        {
            UseShellExecute = true,
            WorkingDirectory = workDir,
            FileName = @"C:\Windows\System32\cmd.exe",
            Arguments = "/c codeql database analyze codeql-dbs csharp-security-and-quality.qls --format=sarif-latest --sarif-category=csharp --output=sast-results.sarif",
            //WindowStyle = ProcessWindowStyle.Hidden
        };
    }

    public void run()
    {
        Process cmd = Process.Start(proc1);
        cmd.WaitForExit();
        cmd = Process.Start(proc2);
        cmd.WaitForExit();
    }
}