mergeInto(LibraryManager.library, {
  RB_ClockMS: function(){return performance.now();},
  RB_SupportsWebTransport: function(){return globalThis.WebTransport && globalThis.isSecureContext ? 1 : 0;},
  $RBLab: {
    next: 1, states: {},
    create: function(){if(Object.keys(RBLab.states).length>=64)return 0;var id=RBLab.next++;RBLab.states[id]={epoch:0,connection:null};return id;},
    close: function(id){var state=RBLab.states[id];if(!state)return;state.epoch++;if(state.connection)state.connection.close();state.connection=null;state.packets=[];state.binary=false;},
    load: function(runtime){
      if(!runtime||runtime==='/transport.js')return import('/transport.js');
      return import(new URL(runtime,globalThis.document?document.baseURI:globalThis.location?location.href:undefined).href);
    },
    open: function(id,url,hello,hash,target,useWT,runtime,binary){
      var state=RBLab.states[id];if(!state)return;
      RBLab.close(id);var attempt=state.epoch;state.target=target;state.binary=!!binary;state.packets=[];
      var failed=false;state.failed=false;
      var active=function(){return RBLab.states[id]===state&&attempt===state.epoch&&!failed;};
      var notify=function(message,receivedMS){
        if(!active())return;
        if(binary){
          if(state.packets.length>=64){state.packets=[];fail('receive_overflow');return;}
          state.packets.push({data:message,receivedMS:receivedMS});
        }else SendMessage(target,'OnTransportMessage',message);
      };
      var fail=function(code){
        if(!active())return;
        var connection=state.connection;failed=true;state.failed=true;state.connection=null;
        if(connection)connection.close();
        if(binary){
          if(state.packets.length>=64){state.packets=[];code='receive_overflow';}
          state.packets.push({error:code,receivedMS:performance.now()});
        }else SendMessage(target,'OnTransportMessage',JSON.stringify({type:'error',code:code}));
      };
      state.fail=fail;
      (async function(){
        try{
          var module=await RBLab.load(runtime);if(!active())return;
          var connection=module.createTransport({kind:useWT?'wt':'ws',url:url,hello:hello,hash:hash,binary:!!binary,receive:notify,closed:fail});
          if(!active()){connection.close();return;}
          state.connection=connection;await connection.ready;
        }catch(e){fail('connection_closed');}
      })();
    },
    send: function(id,data){
      var state=RBLab.states[id];if(!state||state.failed||state.binary)return false;
      var fail=state.fail;if(!state.connection)return false;
      try{
        state.connection.send(data,{waitForSlot:true}).then(function(accepted){if(!accepted)fail('send_failed');},function(){fail('send_failed');});
        return true;
      }catch(e){fail('send_failed');return false;}
    }
  },
  RB_OpenBinary__deps: ['$RBLab'],
  RB_OpenBinary: function(id,url,ptr,length,runtime){
    RBLab.open(id,UTF8ToString(url),HEAPU8.slice(ptr,ptr+length),'','',0,UTF8ToString(runtime),true);
  },
  RB_TrySendBytes__deps: ['$RBLab'],
  RB_TrySendBytes: function(id,ptr,length){
    var state=RBLab.states[id];
    if(!state||!state.binary||state.failed||!state.connection||length<0||length>32768)return 0;
    return state.connection.trySendBytes(HEAPU8.slice(ptr,ptr+length))?1:0;
  },
  RB_PollPacket__deps: ['$RBLab'],
  RB_PollPacket: function(id,ptr,capacity,time,error){
    var state=RBLab.states[id];
    if(!state||!state.binary||!state.packets.length)return -1;
    var packet=state.packets[0];
    if(packet.data&&packet.data.length>capacity)return -1;
    state.packets.shift();
    var codes={connection_closed:1,connection_timeout:2,invalid_server_message:3,receive_overflow:4,send_failed:5,transport_unsupported:6,invalid_client_message:7};
    setValue(time,packet.receivedMS,'double');setValue(error,packet.error?(codes[packet.error]||1):0,'i32');
    if(packet.data)HEAPU8.set(packet.data,ptr);
    return packet.data?packet.data.length:0;
  },
  RB_Create__deps: ['$RBLab'],
  RB_Create: function(){return RBLab.create();},
  RB_Open__deps: ['$RBLab'],
  RB_Open: function(id,url,hello,hash,target,useWT,runtime){RBLab.open(id,UTF8ToString(url),UTF8ToString(hello),UTF8ToString(hash),UTF8ToString(target),useWT,UTF8ToString(runtime));},
  RB_SendTo__deps: ['$RBLab'],
  RB_SendTo: function(id,data){RBLab.send(id,UTF8ToString(data));},
  RB_TrySendTo__deps: ['$RBLab'],
  RB_TrySendTo: function(id,data){return RBLab.send(id,UTF8ToString(data))?1:0;},
  RB_InterruptBinary__deps: ['$RBLab'],
  RB_InterruptBinary: function(id){var state=RBLab.states[id];if(state&&state.binary&&state.fail)state.fail('connection_closed');},
  RB_CloseFor__deps: ['$RBLab'],
  RB_CloseFor: function(id){RBLab.close(id);},
  RB_Destroy__deps: ['$RBLab'],
  RB_Destroy: function(id){RBLab.close(id);delete RBLab.states[id];},
  // Previous single-connection ABI remains isolated in handle 0.
  RB_Close__deps: ['$RBLab'],
  RB_Close: function(){RBLab.close(0);},
  RB_Connect__deps: ['$RBLab'],
  RB_Connect: function(url,hello,hash,target,useWT){
    if(!RBLab.states[0])RBLab.states[0]={epoch:0,connection:null};
    RBLab.open(0,UTF8ToString(url),UTF8ToString(hello),UTF8ToString(hash),UTF8ToString(target),useWT,'/transport.js');
  },
  RB_Send__deps: ['$RBLab'],
  RB_Send: function(data){RBLab.send(0,UTF8ToString(data));}
});
