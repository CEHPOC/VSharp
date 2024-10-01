namespace VSharp.SAST;

public class SarifLoc
{
    public string RuleId;
    public int Startrow;
    public int Startcolumn;
    public Uri Filelocation;
    
    public SarifLoc(string ruleid, int startrow, int startcolumn, Uri filelocation)
    {
        RuleId = ruleid;
        Startrow = startrow;
        Startcolumn = startcolumn;
        Filelocation = filelocation;
    }
}