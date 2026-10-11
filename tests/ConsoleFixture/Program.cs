Console.WriteLine("Console fixture ready");
while (Console.ReadLine() is { } line)
{
    if (line == "exit") break;
    if (line.StartsWith("echo ")) Console.WriteLine(line[5..]);
    else if (line == "bundles") Console.WriteLine("fixture-bundle");
    else Console.WriteLine("Unknown fixture command");
}
