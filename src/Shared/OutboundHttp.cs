using System.Net;
using System.Net.Sockets;
namespace ReplayPanel.Shared;

/// <summary>Public HTTPS only; DNS is validated and the connection is pinned to the validated address.</summary>
public static class OutboundHttp
{
 public static bool PublicAddress(IPAddress ip){
  if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();
  if(IPAddress.IsLoopback(ip))return false;var b=ip.GetAddressBytes();
  if(b.Length==4)return b[0] is not (0 or 10 or 127)&&b[0]<224&&!(b[0]==169&&b[1]==254)&&!(b[0]==172&&b[1]>=16&&b[1]<=31)&&!(b[0]==192&&b[1]==168)&&!(b[0]==100&&b[1]>=64&&b[1]<=127)&&!(b[0]==198&&b[1] is 18 or 19);
  return (b[0]&0xe0)==0x20&&!(b[0]==0x20&&b[1]==0x02)&&!ip.Equals(IPAddress.IPv6None)&&!ip.Equals(IPAddress.IPv6Any)&&!ip.IsIPv6LinkLocal&&!ip.IsIPv6SiteLocal&&!ip.IsIPv6Multicast&&(b[0]&0xfe)!=0xfc;
 }
 public static HttpClient Create(TimeSpan timeout,bool redirects=true){
  var sockets=new SocketsHttpHandler{AllowAutoRedirect=redirects,MaxAutomaticRedirections=5,UseProxy=false,ConnectTimeout=TimeSpan.FromSeconds(15),PooledConnectionLifetime=TimeSpan.FromMinutes(2)};
  sockets.ConnectCallback=async(context,ct)=>{
   var addresses=await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,ct);
   if(addresses.Length==0||addresses.Any(a=>!PublicAddress(a)))throw new HttpRequestException("network.destination");
   Exception? failure=null;
   foreach(var address in addresses){var socket=new Socket(address.AddressFamily,SocketType.Stream,ProtocolType.Tcp);try{await socket.ConnectAsync(new IPEndPoint(address,context.DnsEndPoint.Port),ct);return new NetworkStream(socket,true);}catch(Exception e)when(e is SocketException or IOException){failure=e;socket.Dispose();}catch{socket.Dispose();throw;}}
   throw new HttpRequestException("network.unavailable",failure);
  };
  return new HttpClient(new HttpsOnly(sockets)){Timeout=timeout};
 }
 sealed class HttpsOnly(HttpMessageHandler inner):DelegatingHandler(inner){protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){if(request.RequestUri is not {IsAbsoluteUri:true,Scheme:"https",UserInfo:""})throw new HttpRequestException("network.destination");return base.SendAsync(request,ct);}}
}
