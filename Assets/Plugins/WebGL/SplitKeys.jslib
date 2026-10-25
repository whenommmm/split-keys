mergeInto(LibraryManager.library, {
  $SplitKeysBridge: {
    socket: null,
    gameObject: null,
    callbackName: null,
    scriptLoading: false,
    sessionKey: 'splitkeys:session',
    readSession: function () {
      try {
        var value = window.sessionStorage.getItem(this.sessionKey);
        return value ? JSON.parse(value) : null;
      } catch (_) {
        return null;
      }
    },
    writeSession: function (session) {
      try { window.sessionStorage.setItem(this.sessionKey, JSON.stringify(session)); } catch (_) {}
    },
    clearSession: function () {
      try { window.sessionStorage.removeItem(this.sessionKey); } catch (_) {}
    },
    sendToUnity: function (payload) {
      if (!this.gameObject || !this.callbackName) return;
      SendMessage(this.gameObject, this.callbackName, JSON.stringify(payload));
    },
    loadSocketIo: function () {
      var bridge = this;
      if (typeof window.io === 'function') return bridge.connectSocket();
      if (bridge.scriptLoading) return;
      bridge.scriptLoading = true;
      var script = document.createElement('script');
      script.src = location.origin + '/socket.io/socket.io.js';
      script.onload = function () { bridge.scriptLoading = false; bridge.connectSocket(); };
      script.onerror = function () {
        bridge.scriptLoading = false;
        bridge.sendToUnity({ kind: 'bridgeError', message: 'Could not load the multiplayer client.' });
      };
      document.head.appendChild(script);
    },
    connectSocket: function () {
      var bridge = this;
      if (bridge.socket) bridge.socket.disconnect();
      bridge.socket = window.io({ autoConnect: true });
      bridge.socket.on('connect', function () {
        bridge.sendToUnity({ kind: 'connected' });
        var saved = bridge.readSession();
        var room = new URLSearchParams(location.search).get('room') || '';
        if (saved && saved.code && saved.playerId && saved.name && room.toUpperCase() === saved.code.toUpperCase()) {
          bridge.socket.emit('join_room', saved, function (data) {
            if (data && data.ok) bridge.sendToUnity({ kind: 'ack', eventName: 'resume_room', data: data });
            else {
              if (data && data.error === 'NOT_FOUND') bridge.clearSession();
              bridge.sendToUnity({ kind: 'ack', eventName: 'resume_room', data: data || {} });
            }
          });
        }
      });
      bridge.socket.on('disconnect', function () { bridge.sendToUnity({ kind: 'disconnected' }); });
      bridge.socket.on('connect_error', function () {
        bridge.sendToUnity({ kind: 'bridgeError', message: 'Cannot reach the game server.' });
      });
      bridge.socket.on('state', function (state) { bridge.sendToUnity({ kind: 'state', state: state }); });
      var room = new URLSearchParams(location.search).get('room') || '';
      bridge.sendToUnity({ kind: 'invite', roomCode: room });
    },
    emit: function (eventName, payload, hasPayload) {
      var bridge = this;
      if (!bridge.socket || !bridge.socket.connected) {
        bridge.sendToUnity({ kind: 'bridgeError', message: 'Not connected to the game server.' });
        return;
      }
      var done = function (data) {
        if (data && data.ok && (eventName === 'create_room' || eventName === 'join_room')) {
          bridge.writeSession({ code: data.code, playerId: data.playerId, name: payload.name });
        }
        if (eventName === 'leave_room' && data && data.ok) bridge.clearSession();
        if (eventName === 'join_room' && data && data.error === 'NOT_FOUND') bridge.clearSession();
        bridge.sendToUnity({ kind: 'ack', eventName: eventName, data: data || {} });
      };
      if (hasPayload) bridge.socket.emit(eventName, payload, done);
      else bridge.socket.emit(eventName, done);
    },
    copyInvite: function (url) {
      var bridge = this;
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(url).then(function () {
          bridge.sendToUnity({ kind: 'shared', message: 'Link copied!' });
        }).catch(function () {
          bridge.sendToUnity({ kind: 'shared', message: url });
        });
      } else {
        bridge.sendToUnity({ kind: 'shared', message: url });
      }
    }
  },

  SK_Connect__deps: ['$SplitKeysBridge'],
  SK_Connect: function (gameObjectPtr, callbackPtr) {
    SplitKeysBridge.gameObject = UTF8ToString(gameObjectPtr);
    SplitKeysBridge.callbackName = UTF8ToString(callbackPtr);
    SplitKeysBridge.loadSocketIo();
  },
  SK_CreateRoom__deps: ['$SplitKeysBridge'],
  SK_CreateRoom: function (namePtr) {
    SplitKeysBridge.emit('create_room', { name: UTF8ToString(namePtr) }, true);
  },
  SK_JoinRoom__deps: ['$SplitKeysBridge'],
  SK_JoinRoom: function (codePtr, namePtr) {
    SplitKeysBridge.emit('join_room', {
      code: UTF8ToString(codePtr),
      name: UTF8ToString(namePtr)
    }, true);
  },
  SK_Press__deps: ['$SplitKeysBridge'],
  SK_Press: function (letterPtr, atIndex) {
    SplitKeysBridge.emit('press', {
      letter: UTF8ToString(letterPtr),
      atIndex: atIndex
    }, true);
  },
  SK_StartGame__deps: ['$SplitKeysBridge'],
  SK_StartGame: function () {
    SplitKeysBridge.emit('start_game', null, false);
  },
  SK_AddBot__deps: ['$SplitKeysBridge'],
  SK_AddBot: function () {
    SplitKeysBridge.emit('add_bot', null, false);
  },
  SK_RemoveBot__deps: ['$SplitKeysBridge'],
  SK_RemoveBot: function () {
    SplitKeysBridge.emit('remove_bot', null, false);
  },
  SK_LeaveRoom__deps: ['$SplitKeysBridge'],
  SK_LeaveRoom: function () {
    SplitKeysBridge.emit('leave_room', null, false);
  },
  SK_SetRoomQuery: function (codePtr) {
    var code = UTF8ToString(codePtr);
    var url = new URL(location.href);
    if (code) url.searchParams.set('room', code);
    else url.searchParams.delete('room');
    history.replaceState({}, '', url.toString());
  },
  SK_RequestInviteCode__deps: ['$SplitKeysBridge'],
  SK_RequestInviteCode: function () {
    var room = new URLSearchParams(location.search).get('room') || '';
    SplitKeysBridge.sendToUnity({ kind: 'invite', roomCode: room });
  },
  SK_Invite__deps: ['$SplitKeysBridge'],
  SK_Invite: function (codePtr) {
    var code = UTF8ToString(codePtr);
    var url = new URL(location.href);
    url.searchParams.set('room', code);
    var inviteUrl = url.toString();
    var bridge = SplitKeysBridge;
    if (navigator.share) {
      navigator.share({ title: 'Join my Split Keys room', url: inviteUrl })
        .then(function () { bridge.sendToUnity({ kind: 'shared', message: 'Invite shared.' }); })
        .catch(function () { bridge.copyInvite(inviteUrl); });
    } else {
      bridge.copyInvite(inviteUrl);
    }
  }
});
