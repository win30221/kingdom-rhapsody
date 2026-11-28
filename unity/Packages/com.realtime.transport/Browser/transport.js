// Shared by the diagnostic page and Unity WebGL. Every async operation belongs
// to this object; closing it makes late opens, reads and writes inert.
export const maxMessageBytes=32*1024;
export function createTransport({kind,url,hello,hash,sent,receive,closed,binary=false,clock=()=>performance.now()}) {
  let stopped=false, socket, wt, datagrams, sending=false, controlReceived=false;
  let writeFinished,slotWaiting=false;
  const pendingWrites=new Set();
  const encoder=new TextEncoder();
  const validBytes=data=>data instanceof Uint8Array&&data.byteLength<=maxMessageBytes;
  const validText=data=>typeof data==='string'&&encoder.encode(data).byteLength<=maxMessageBytes;
  let resolveReady,rejectReady;
  const ready=new Promise((resolve,reject)=>{resolveReady=resolve;rejectReady=reject;});
  // A synchronous unsupported/cancel callback may dispose the caller before it
  // awaits ready. The original promise still rejects for awaiting callers.
  ready.catch(()=>{});
  const timer=setTimeout(()=>fail('connection_timeout'),8000);
  function close() {
    if(stopped)return;
    stopped=true;for(const cancel of pendingWrites)cancel();clearTimeout(timer);rejectReady(Error('cancelled'));
    try{socket?.close();}catch{}
    try{wt?.close();}catch{}
  }
  function fail(code='connection_closed') { if(stopped)return;close();closed(code); }
  function opened() {if(!stopped){clearTimeout(timer);resolveReady();}}
  function message(data) {
    if(stopped)return;
    if(binary){
      if(!(data instanceof ArrayBuffer)||data.byteLength>maxMessageBytes){fail('invalid_server_message');return;}
      receive(new Uint8Array(data).slice(),clock());return;
    }
    if(!validText(data)){fail('invalid_server_message');return;}
    receive(data,clock());
  }
  function waitForWrite(write) {
    return new Promise((resolve,reject)=>{
      const cancel=()=>{pendingWrites.delete(cancel);resolve();};
      pendingWrites.add(cancel);
      // Remove completed waits so a long-lived connection does not retain a
      // cancellation reaction for every datagram it has ever submitted.
      Promise.resolve(write).then(value=>{pendingWrites.delete(cancel);resolve(value);},error=>{pendingWrites.delete(cancel);reject(error);});
    });
  }
  const connection={ready,close,trySendBytes(data){
    if(!binary)throw new TypeError('Transport is in text mode');
    if(!validBytes(data))throw new RangeError('Message exceeds transport binary limit');
    if(stopped||!socket||socket.readyState!==1||socket.bufferedAmount>=4096)return false;
    const owned=data.slice();
    try{socket.send(owned);sent?.(owned,clock());return true;}catch{fail('send_failed');return false;}
  },async send(data,{waitForSlot=false}={}){
    if(binary)return connection.trySendBytes(data);
    if(!validText(data))throw new RangeError('Message exceeds transport text limit');
    if(stopped)return false;
    if(socket){if(socket.readyState!==1||socket.bufferedAmount>=4096)return false;sent?.(data,clock());socket.send(data);return true;}
    if(!datagrams)return false;
    if(sending){
      // Opt in to one bounded waiting slot. Closing must release it even if
      // the platform never settles its current write.
      if(!waitForSlot||slotWaiting)return false;
      slotWaiting=true;
      try{await waitForWrite(writeFinished);}catch{return false;}finally{slotWaiting=false;}
      if(stopped||sending)return false;
    }
    sending=true;
    try{sent?.(data,clock());writeFinished=datagrams.write(new TextEncoder().encode(data));await waitForWrite(writeFinished);return !stopped;}
    catch{fail('send_failed');return false;}
    finally{sending=false;}
  }};
  (async()=>{
    try{
      if(binary&&kind!=='ws'){fail('transport_unsupported');return;}
      if(!(binary?validBytes(hello):validText(hello))){fail('invalid_client_message');return;}
      if(kind==='ws'){
        if(binary)hello=hello.slice();
        socket=new WebSocket(url);
        if(binary)socket.binaryType='arraybuffer';
        socket.onopen=()=>{if(stopped){socket.close();return;}socket.send(hello);opened();};
        socket.onmessage=e=>message(e.data);
        socket.onclose=()=>fail();socket.onerror=()=>fail();
        return;
      }
      if(kind!=='wt'||!globalThis.WebTransport){fail('transport_unsupported');return;}
      if(!/^[a-f\d]{64}$/i.test(hash)){fail('invalid_certificate_hash');return;}
      const fingerprint=Uint8Array.from(hash.match(/../g),h=>parseInt(h,16));
      wt=new WebTransport(url,{serverCertificateHashes:[{algorithm:'sha-256',value:fingerprint}]});
      wt.closed.then(()=>fail(),()=>fail());
      await wt.ready;if(stopped)return;
      const stream=await wt.createBidirectionalStream();if(stopped)return;
      const writer=stream.writable.getWriter();
      await writer.write(new TextEncoder().encode(hello+'\n'));if(stopped)return;
      datagrams=wt.datagrams.writable.getWriter();opened();
      void (async()=>{
        const reader=stream.readable.getReader(),decoder=new TextDecoder('utf-8',{fatal:true});let pending='';
        try{while(!stopped){
          const {value,done}=await reader.read();if(stopped)return;
          if(done){pending+=decoder.decode();if(pending.length>0||!controlReceived)fail();return;}
          pending+=decoder.decode(value,{stream:true});
          let at;while((at=pending.indexOf('\n'))>=0){
            if(encoder.encode(pending.slice(0,at)).byteLength>maxMessageBytes){fail('invalid_server_message');return;}
            controlReceived=true;message(pending.slice(0,at));pending=pending.slice(at+1);
          }
          if(encoder.encode(pending).byteLength>maxMessageBytes){fail('invalid_server_message');return;}
        }}catch{fail();}finally{reader.releaseLock();}
      })();
      const reader=wt.datagrams.readable.getReader(),decoder=new TextDecoder('utf-8',{fatal:true});
      try{while(!stopped){const {value,done}=await reader.read();if(stopped)return;if(done){fail();return;}if(value.byteLength>maxMessageBytes){fail('invalid_server_message');return;}message(decoder.decode(value));}}
      finally{reader.releaseLock();}
    }catch{fail();}
  })();
  return connection;
}
