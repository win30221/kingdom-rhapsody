#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Realtime.Transport
{
    // Optional desktop process adapter; QUIC is owned by the Go helper, not Unity.
    internal sealed class NativeWebTransport:IDisposable
    {
        private readonly Process process;
        private readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        private readonly Action<string> receive;
        private readonly Action failed;
        private Task reading = Task.CompletedTask, draining = Task.CompletedTask;
        private int disposed;
        private static readonly UTF8Encoding UTF8=new UTF8Encoding(false,true);
        public NativeWebTransport(string executable,string url,string hello,string hash,Action<string> receive,Action failed)
        {
            if(!Path.IsPathRooted(executable)||!File.Exists(executable))throw new FileNotFoundException("Configure an absolute native WebTransport helper path");
            if(!Uri.TryCreate(url,UriKind.Absolute,out var address)||address.Scheme!="https"||address.UserInfo.Length>0||address.Fragment.Length>0)throw new ArgumentException("WebTransport requires an HTTPS endpoint");
            if(hash==null||hash.Length!=64)throw new ArgumentException("WebTransport requires a SHA-256 certificate pin");
            foreach(char c in hash)if(!Uri.IsHexDigit(c))throw new ArgumentException("Invalid certificate pin");
            if(hello.IndexOfAny(new[]{'\r','\n'})>=0)throw new ArgumentException("Hello must fit a reliable line");
            this.receive=receive;this.failed=failed;
            process=new Process{StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
            try{
                if(!process.Start())throw new IOException("Native WebTransport helper did not start");
                // Arguments and environment never contain the URL or hello credentials.
                process.StandardInput.WriteLine("RFWT1");
                process.StandardInput.WriteLine(Convert.ToBase64String(UTF8.GetBytes(url)));
                process.StandardInput.WriteLine(hash);
                process.StandardInput.WriteLine(Convert.ToBase64String(UTF8.GetBytes(hello)));
                process.StandardInput.Flush();
                reading=ReadMessages();draining=DrainErrors();
            }catch{Dispose();throw;}
        }
        public async Task Send(string text)
        {
            if(lifetime.IsCancellationRequested)return;
            await process.StandardInput.WriteLineAsync("D "+Convert.ToBase64String(UTF8.GetBytes(text))).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        private async Task ReadMessages()
        {
            try{
                var bytes=new byte[4096];var line=new StringBuilder();int bound=4*((MessageTransport.MaxMessageBytes+2)/3)+4;
                while(!lifetime.IsCancellationRequested){
                    int count=await process.StandardOutput.BaseStream.ReadAsync(bytes,0,bytes.Length,lifetime.Token).ConfigureAwait(false);
                    if(count==0)throw new IOException("Helper closed");
                    for(int i=0;i<count;i++){
                        byte value=bytes[i];
                        if(value=='\n'){
                            string text=line.ToString();line.Clear();
                            if(!text.StartsWith("M ",StringComparison.Ordinal))throw new IOException("Helper error");
                            byte[] payload=Convert.FromBase64String(text.Substring(2));
                            if(payload.Length>MessageTransport.MaxMessageBytes)throw new IOException("Helper message limit");
                            receive(UTF8.GetString(payload));
                        }else{
                            if(value>127||line.Length>=bound)throw new IOException("Invalid helper framing");
                            if(value!='\r')line.Append((char)value);
                        }
                    }
                }
            }catch(Exception){if(!lifetime.IsCancellationRequested)failed();}
        }
        private async Task DrainErrors()
        {
            try{var bytes=new byte[1024];while(!lifetime.IsCancellationRequested&&await process.StandardError.BaseStream.ReadAsync(bytes,0,bytes.Length,lifetime.Token).ConfigureAwait(false)>0){}}
            catch(Exception){}
        }
        public void Dispose()
        {
            if(Interlocked.Exchange(ref disposed,1)!=0)return;lifetime.Cancel();
            try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
            process.Dispose();
            // Readers observe cancellation before its source is disposed. Never
            // block Unity's owner waiting for process pipes to finish.
            _=ReleaseLifetime();
        }
        private async Task ReleaseLifetime()
        { try { await Task.WhenAll(reading,draining).ConfigureAwait(false); } finally { lifetime.Dispose(); } }
    }
}
#endif
