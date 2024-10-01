using VSharp.SAST;
using VSharp;

public static class Program
{
    static void Main(string[] args)
    {
        var (methods, rules) = Converter.RunAndConvert(args);
        var stat = TestGenerator.Cover(methods,new VSharpOptions(searchStrategy:SearchStrategy.Guided));
    }
}