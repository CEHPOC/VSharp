using System.Reflection;
using System.Runtime.CompilerServices;
using VSharp;

namespace VSharp.SAST
{
    public static class Converter
    {
        public static Dictionary<codeLocation,string> rules;
        public static (HashSet<MethodBase>, Dictionary<codeLocation, string>) RunAndConvert(string[] args)
        {
            string projectpath = args[0];
            string slnname;
            if (projectpath[projectpath.Length - 1] != '\\')
                projectpath = projectpath + "\\";
            slnname = projectpath.Split("\\")[projectpath.Split("\\").Length - 2];
            string path1 = projectpath + slnname + ".sln";
            string path2 = projectpath;
            string path3 = projectpath + "sast-results.sarif";

            var publishRunner = new PublishRunner(path2);
            publishRunner.run();

            var runner = new SecurityCodeScan(path1, path2);
            runner.run();

            SarifParser sarifpars = new SarifParser();
            List<SarifLoc> list = sarifpars.GetLocationsFromSarif(path2, path3);
            
            //А можно ли без win-x64?
            string dllpath = projectpath + slnname + "\\bin\\Release\\net7.0\\win-x64\\publish\\" + slnname + ".dll";
            
            var assembly = AssemblyManager.LoadFromAssemblyPath(dllpath);

            var sp = new SequencePointsStorage(dllpath);
            sp.MappingSarifLocCodeLoc(list, assembly);

            rules = sp.rules;
            return (sp.methods, sp.rules);
        }
    }
}