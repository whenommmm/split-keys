mergeInto(LibraryManager.library, {
  $SignalNoiseBridge: {
    socket: null,
    gameObject: null,
    callback: null,
    send: function (type, text) {
      if (this.socket && this.socket.readyState === WebSocket.OPEN) {
        this.socket.send(JSON.stringify({ type: UTF8ToString(type), text: UTF8ToString(text) }));
      }
    },
    deliver: function (packet) {
      if (!this.gameObject || !this.callback) return;
      SendMessage(this.gameObject, this.callback, JSON.stringify(packet));
    }
  },
  SN_Connect: function (hostPtr, callbackPtr, urlPtr) {
    var host = UTF8ToString(hostPtr);
    var callback = UTF8ToString(callbackPtr);
    var url = UTF8ToString(urlPtr);
    SignalNoiseBridge.gameObject = host;
    SignalNoiseBridge.callback = callback;
    if (SignalNoiseBridge.socket) SignalNoiseBridge.socket.close();
    var protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    SignalNoiseBridge.socket = new WebSocket(protocol + '//' + location.host + url);
    SignalNoiseBridge.socket.onopen = function () { SignalNoiseBridge.deliver({ type: 'connected' }); };
    SignalNoiseBridge.socket.onmessage = function (event) {
      try { SignalNoiseBridge.deliver(JSON.parse(event.data)); }
      catch (e) { SignalNoiseBridge.deliver({ type: 'bridgeError', message: 'Received invalid server data.' }); }
    };
    SignalNoiseBridge.socket.onclose = function () { SignalNoiseBridge.deliver({ type: 'disconnected' }); };
    SignalNoiseBridge.socket.onerror = function () { SignalNoiseBridge.deliver({ type: 'bridgeError', message: 'Connection failed. Check the room code and try again.' }); };
  },
  SN_Send: function (packetPtr) {
    if (SignalNoiseBridge.socket && SignalNoiseBridge.socket.readyState === WebSocket.OPEN) {
      SignalNoiseBridge.socket.send(UTF8ToString(packetPtr));
    }
  },
  SN_Close: function () {
    if (SignalNoiseBridge.socket) SignalNoiseBridge.socket.close();
    SignalNoiseBridge.socket = null;
  }
});
