using Microsoft.CodeAnalysis.Sarif;

namespace VSharp.SAST;

public class SarifParser
{
    public List<SarifLoc> SarifList;

    public SarifParser()
    {
        SarifList = new List<SarifLoc>();
    }
    
    public List<SarifLoc> GetLocationsFromSarif(string sarifname)
    {
        SarifLog log = SarifLog.Load(sarifname);
        foreach (Run r in log.Runs)
        {
            foreach (Result result in r.Results)
            {
                SarifLoc loc = new SarifLoc(
                    result.RuleId,
                    result.Locations[0].PhysicalLocation.Region.StartLine,
                    result.Locations[0].PhysicalLocation.Region.StartColumn,
                    result.Message.Text,
                    result.Locations[0].PhysicalLocation.ArtifactLocation.Uri
                    );
                
                SarifList.Add(loc);
            }
        }

        return SarifList;
    }
}