namespace ReplayPanel.Supervisor;
public static class ErrorCodes
{
    public static string From(Exception error)
    {
        if(System.Text.RegularExpressions.Regex.IsMatch(error.Message,@"\A[a-z]+\.[a-zA-Z]+\z"))return error.Message;
        if(error is UnauthorizedAccessException)return "path.readonly";
        if(error is OperationCanceledException or TimeoutException)return "request.timeout";
        if(error.Message.Contains("bereits"))return "server.running";
        if(error.Message.Contains("Extern"))return "server.external";
        if(error.Message.Contains("Steuerzeichen")||error.Message.Contains("Stop und Update"))return "command.invalid";
        if(error.Message.Contains("Bundle-Status"))return "bundle.changed";
        if(error.Message.Contains("nicht gefunden"))return "bundle.missing";
        if(error.Message.Contains("Bundle-Aktion"))return "bundle.invalid";
        if(error.Message.Contains("bestätigt"))return "bundle.timeout";
        if(error.Message.Contains("Failed"))return "bundle.failed";
        if(error.Message.Contains("exit"))return "server.stopTimeout";
        return "request.failed";
    }
}
