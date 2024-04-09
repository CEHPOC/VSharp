using VSharp.SAST;
using System.Reflection;


public static class Program
{
    static void Main(string[] args)
    {
        string projectpath = args[0];
        string slnname;
        if (projectpath[projectpath.Length - 1] != '\\')
            projectpath = projectpath + "\\";
        slnname = projectpath.Split("\\")[projectpath.Split("\\").Length-2];
        string path1 = projectpath + slnname + ".sln";
        string path2 = projectpath;
        string path3 = projectpath + "sast-results.sarif";

        var runner = new SecurityCodeScan(path1, path2);
        runner.run();

        SarifParser sarifpars = new SarifParser();
        List<SarifLoc> list = sarifpars.GetLocationsFromSarif(path3);
        foreach (var x in list)
        {
            Console.WriteLine($"{x.RuleId} {x.Startrow} {x.Startcolumn} {x.Filelocation}");
        }

        var sample = Assembly.LoadFrom(projectpath + slnname + "\\bin\\Debug\\net6.0\\" + slnname + ".dll");
        var factory = new CodeLocationFactory(projectpath + slnname + "\\bin\\Debug\\net6.0\\" + slnname + ".pdb");
        List<(int,int)> result = factory.GetCodeLocations(sample, list);

        foreach (var r in result)
        {
            Console.WriteLine(r);
        }

    }
}
