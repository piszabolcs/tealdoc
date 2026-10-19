using DemoCorpus;

// Használat:  dotnet run --project tools/DemoCorpus -- [célmappa]   (alapértelmezés: demo/corpus)
string target = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("demo", "corpus"));
try
{
    int files = DemoCorpusGenerator.Generate(target);
    Console.WriteLine($"Kész: {files} kitalált fájl itt: {target}");
    return 0;
}
catch (IOException ex)
{
    Console.Error.WriteLine("Hiba: " + ex.Message);
    return 1;
}
