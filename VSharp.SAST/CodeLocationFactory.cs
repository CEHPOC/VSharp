using VSharp;
using VSharp.CSharpUtils;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace VSharp.SAST;

public class CodeLocationFactory
{
    public string PdbPath;
    
    public CodeLocationFactory(string pdbpath)
    {
        PdbPath = pdbpath;
    }
    
    public List<SarifLoc> FilterCodeLocations(List<SarifLoc> list)
    {
        var reslist = new List<SarifLoc>();
        foreach (var loc in list)
        {
            if (loc.RuleId == "SCS0002")
            {
                reslist.Add(loc);
            }
        }
        return reslist;
    }

    //parse method name from error message
    public string FindMethodName(string text)
    {
        string[] mas = text.Split("'");
        string s = mas[mas.Length - 2];
        mas = s.Split(".");
        s = mas[mas.Length - 1];
        s = s.Split("(")[0];
        return s;
    }
    
    public List<(int, int)> GetCodeLocations(Assembly assembly, List<SarifLoc> list)
    {
        var filteredlist = FilterCodeLocations(list);
        List<(int,int)> result = new List<(int,int)>();

        foreach (var loc in filteredlist)
        {
            string methodName = FindMethodName(loc.Text);
            var method = ReflectionUtils.ResolveMethod(assembly,methodName);
            int token = method.MetadataToken;
            int offset = -1;
            int offsetend = -1;
            (offset, offsetend) = ReadSourceLineData(token,loc.Startrow,loc.Startcolumn);
            result.Add((offset, offsetend));
        }

        return result;
    }

    public bool BelongingCodePositionToSequencePoint(int row, int column, SequencePoint sp)
    {
        if (row > sp.StartLine && row < sp.EndLine)
            return true;
        if (row == sp.StartLine && column > sp.StartColumn)
            return true;
        if (row == sp.EndLine && column < sp.EndColumn)
            return true;
        return false;
    }
    
    //search offset of code position
    public (int, int) ReadSourceLineData(int methodToken, int startrow, int startcolumn)
    {
        // Determine method row number
        EntityHandle ehMethod = MetadataTokens.EntityHandle(methodToken);

        if (ehMethod.Kind != HandleKind.MethodDefinition)
        {
            Console.WriteLine($"Invalid token kind: {ehMethod.Kind}");
            return (-1,-1);
        }

        int rowNumber = MetadataTokens.GetRowNumber(ehMethod);

        // MethodDebugInformation table is indexed by same row numbers as MethodDefinition table
        MethodDebugInformationHandle hDebug = MetadataTokens.MethodDebugInformationHandle(rowNumber);

        // Open Portable PDB file
        using var fs = new FileStream(PdbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(fs);
        MetadataReader reader = provider.GetMetadataReader();

        if (rowNumber > reader.MethodDebugInformation.Count)
        {
            Console.WriteLine("Error: Method row number is out of range");
            return (-1,-1);
        }

        // Print source line information as console table
        MethodDebugInformation di = reader.GetMethodDebugInformation(hDebug);

        int result = -1;
        int endresult = -1;
        bool f = false;
        var sps = di.GetSequencePoints();
        foreach (SequencePoint sp in sps)
        {
            if (f)
            {
                endresult = sp.Offset;
                break;
            }
            if (BelongingCodePositionToSequencePoint(startrow, startcolumn, sp))
            {
                result = sp.Offset;
                f = true;
            }
        }
        return (result,endresult);
    }
}