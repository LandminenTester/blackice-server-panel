using System.Text.Json;
using ReplayPanel.Shared;
using ReplayPanel.Supervisor;
var path=args[0];var id=args[1];var version=args[2];
File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!,"panel-update.json"),JsonSerializer.Serialize(new UpdateJob(id,"applying","restart"),PanelConfig.Json));
await new DockerPanelUpdater(PanelConfig.Load(path),path).Apply(id,version);
