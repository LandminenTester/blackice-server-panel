using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ReplayPanel.Shared;
public static class Permissions {
 public static readonly string[] All=["server.start","server.stop","server.restart","server.force-stop","console.read","console.write","logs.read","bundles.read","bundles.start","bundles.stop","bundles.restart","settings.write","users.manage"];
}
public sealed class PanelUser {
 public string Username {get;set;}="";
 public string PasswordHash {get;set;}="";
 public string PasswordSalt {get;set;}="";
 public string[] Permissions {get;set;}=[];
 public bool Enabled {get;set;}=true;
 public string Stamp {get;set;}=Guid.NewGuid().ToString("N");
 public void SetPassword(string password){PasswordSalt=Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));PasswordHash=Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(PasswordSalt),210000,HashAlgorithmName.SHA256,32));}
 public bool Verify(string password)=>Enabled&&CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(PasswordSalt),210000,HashAlgorithmName.SHA256,32),Convert.FromBase64String(PasswordHash));
 public UserView View()=>new(Username,Permissions,Enabled,Stamp);
}
public sealed record UserView(string Username,string[] Permissions,bool Enabled,string Stamp);
public sealed record UserChange(string Username,string? Password,string[] Permissions,bool Enabled=true,bool Create=false);
public static class Accounts {
 private static List<PanelUser> Current(PanelConfig c)=>c.Users??[new(){Username=c.Username,PasswordHash=c.PasswordHash,PasswordSalt=c.PasswordSalt,Permissions=Permissions.All,Stamp="legacy-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.PasswordHash))),Enabled=c.Configured}];
 public static UserView? Authenticate(PanelConfig c,string username,string password){lock(c){var user=Current(c).FirstOrDefault(u=>u.Username.Equals(username,StringComparison.OrdinalIgnoreCase));return user is not null&&user.Verify(password)?user.View():null;}}
 public static UserView? Session(PanelConfig c,string username,string stamp){lock(c){return Current(c).FirstOrDefault(u=>u.Enabled&&u.Username.Equals(username,StringComparison.OrdinalIgnoreCase)&&u.Stamp==stamp)?.View();}}
 public static UserView[] List(PanelConfig c){lock(c){return Current(c).Select(u=>u.View()).ToArray();}}
 public static void Save(PanelConfig c,string path,UserChange input){lock(c){
  if(!Regex.IsMatch(input.Username,"^[A-Za-z0-9][A-Za-z0-9_.-]{2,31}$")||input.Permissions is null||input.Permissions.Any(p=>!Permissions.All.Contains(p)))throw new InvalidOperationException("users.invalid");
  if(input.Password is not null&&input.Password.Length is <8 or >256)throw new InvalidOperationException("users.password");
  var users=Current(c).Select(u=>JsonSerializer.Deserialize<PanelUser>(JsonSerializer.Serialize(u))!).ToList();
  var user=users.FirstOrDefault(u=>u.Username.Equals(input.Username,StringComparison.OrdinalIgnoreCase));
  if(input.Create&&user is not null)throw new InvalidOperationException("users.exists");
  if(!input.Create&&user is null)throw new InvalidOperationException("users.missing");
  if(user is null){if(input.Password is null)throw new InvalidOperationException("users.password");user=new(){Username=input.Username};users.Add(user);}
  user.Permissions=input.Permissions.Distinct().ToArray();user.Enabled=input.Enabled;user.Stamp=Guid.NewGuid().ToString("N");if(input.Password is not null)user.SetPassword(input.Password);
  Commit(c,path,users);
 }}
 public static void Delete(PanelConfig c,string path,string username){lock(c){var users=Current(c).Where(u=>!u.Username.Equals(username,StringComparison.OrdinalIgnoreCase)).ToList();if(users.Count==Current(c).Count)throw new InvalidOperationException("users.missing");Commit(c,path,users);}}
 private static void Commit(PanelConfig c,string path,List<PanelUser> users){if(!users.Any(u=>u.Enabled&&u.Permissions.Contains("users.manage")))throw new InvalidOperationException("users.lastAdmin");var previous=c.Users;c.Users=users;try{PanelConfig.Save(path,c);}catch{c.Users=previous;throw;}}
}
