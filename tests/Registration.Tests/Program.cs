using System;
using System.IO;
using System.Text.Json;
using WeldingTrainer.Content.Spatial;
using WeldingTrainer.Registration.Tests;

sealed class Codec : ISpatialJson
{
    public static readonly JsonSerializerOptions Options=new JsonSerializerOptions { IncludeFields=true, IgnoreReadOnlyProperties=true };
    public T Read<T>(string json) => JsonSerializer.Deserialize<T>(json,Options);
}
static class Program
{
    static int Main(string[] args)
    {
        var suite=new RegistrationTestCases(args.Length==1?Path.GetFullPath(args[0]):Directory.GetCurrentDirectory(),new Codec(),x=>JsonSerializer.Serialize(x,x.GetType(),Codec.Options));
        int passed=0,failed=0;
        foreach(var test in suite.Cases())
        {
            try { test.Value(); passed++; Console.WriteLine("PASS "+test.Key); }
            catch(Exception error) { failed++; Console.Error.WriteLine("FAIL "+test.Key+": "+error); }
        }
        Console.WriteLine($"{passed} passed; {failed} failed."); return failed==0?0:1;
    }
}
