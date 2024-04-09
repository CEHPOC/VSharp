namespace VSharp.SAST;

public class SarifLoc
{
    public string RuleId;
    public int Startrow;
    public int Startcolumn;
    public string Text;
    public Uri Filelocation;

    public SarifLoc(string ruleid, int startrow, int startcolumn)
    {
        RuleId = ruleid;
        Startrow = startrow;
        Startcolumn = startcolumn;
    }
    
    public SarifLoc(string ruleid, int startrow, int startcolumn, string text, Uri filelocation)
    {
        RuleId = ruleid;
        Startrow = startrow;
        Startcolumn = startcolumn;
        Text = text;
        Filelocation = filelocation;
    }
}