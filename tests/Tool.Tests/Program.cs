using System;
using WeldingTrainer.Platform.Meta.Tool.Tests;
int passed = 0, failed = 0;
foreach (var test in ToolTestCases.Cases())
{
    try { test.Value(); passed++; Console.WriteLine("PASS " + test.Key); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Key + ": " + error); }
}
Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
