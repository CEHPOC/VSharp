using System.Reflection;
using Mono;
using Mono.Cecil;
using VSharp.CSharpUtils;
using AssemblyDefinition = Mono.Cecil.AssemblyDefinition;
using SequencePoint = Mono.Cecil.Cil.SequencePoint;

namespace VSharp.SAST;

public class SequencePointsStorage
{
    private Dictionary<Uri, HashSet<SequencePoint>> storage = new();
    private ReaderParameters parameters = new() { ReadSymbols = true };
    public HashSet<MethodBase> methods = new();
    public Dictionary<codeLocation, string> rules = new();
    private HashSet<codeLocation> _codeLocations = new();

    public SequencePointsStorage(string assemblypath)
    {
        var ad = AssemblyDefinition.ReadAssembly(assemblypath, parameters);
        var methods = ad.Modules
            .SelectMany(m => m.GetTypes())
            .SelectMany(t => t.Methods.Where(m => m.HasBody));

        var sps = methods.Where(m => m.DebugInformation.HasSequencePoints)
            .SelectMany(m => m.DebugInformation.SequencePoints);
        foreach (var sp in sps)
        {
            if (!storage.ContainsKey(new Uri(sp.Document.Url)))
            {
                storage[new Uri(sp.Document.Url)]=new HashSet<SequencePoint>();
            }

            storage[new Uri(sp.Document.Url)].Add(sp);
        }
    }

    public List<SarifLoc> FilterCodeLocations(List<SarifLoc> list)
        {
            var reslist = new List<SarifLoc>();
            foreach (var loc in list)
            {
                
                if (loc.RuleId == "cs/catch-nullreferenceexception")
                {
                    reslist.Add(loc);
                }
                
                if (loc.RuleId == "cs/index-out-of-bounds")
                {
                    reslist.Add(loc);
                }
            }
            return reslist;
        }
    
    public void MappingSarifLocCodeLoc(List<SarifLoc> list, Assembly assembly)
    {
        var filteredList = FilterCodeLocations(list);
        foreach (var sloc in filteredList)
        {
            var sps = storage[sloc.Filelocation];
            foreach (var sp in sps)
            {
                if (!BelongingCodePositionToSequencePoint(sp.StartLine, sp.StartColumn, sp))
                {
                    var meth = ReflectionUtils.ResolveMethod(assembly, sp.Document.MetadataToken.ToInt32());
                    var cd = new codeLocation(sp.Offset,Application.getMethod(meth));
                    _codeLocations.Add(cd);
                    rules[cd] = sloc.RuleId;
                    if(!methods.Contains(meth))
                        methods.Add(meth);
                }
            }
        }
    }
    
    private bool BelongingCodePositionToSequencePoint(int row, int column, SequencePoint sp)
    {
        if(sp.StartLine == sp.EndLine)
            if (row == sp.StartLine && sp.StartColumn <= column && column < sp.EndColumn)
                return true;
            else 
                return false;
        if (row > sp.StartLine && row < sp.EndLine)
            return true;
        if (row == sp.StartLine && column >= sp.StartColumn)
            return true;
        if (row == sp.EndLine && column < sp.EndColumn)
            return true;
        return false;
    }
}