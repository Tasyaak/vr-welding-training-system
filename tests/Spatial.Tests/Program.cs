using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WeldingTrainer.Content.Spatial;
using WeldingTrainer.Content.Spatial.Tests;

sealed class JsonCodec : ISpatialJson
{
    internal static readonly JsonSerializerOptions Options=new JsonSerializerOptions { IncludeFields=true, IgnoreReadOnlyProperties=true };
    public T Read<T>(string json) => JsonSerializer.Deserialize<T>(json,Options);
}
static class Program
{
    static int Main(string[] args)
    {
        string root=args.Length==1?Path.GetFullPath(args[0]):Directory.GetCurrentDirectory();
        var suite=new SpatialTestCases(root,new JsonCodec(),value=>JsonSerializer.Serialize(value,value.GetType(),JsonCodec.Options));
        int failed=0;
        foreach(var test in suite.Cases())
        {
            try { test.Value(); Console.WriteLine("PASS "+test.Key); }
            catch(Exception error) { failed++; Console.Error.WriteLine("FAIL "+test.Key+": "+error); }
        }
        Console.WriteLine($"{suite.Cases().Count()-failed} passed; {failed} failed."); return failed==0?0:1;
    }
}
