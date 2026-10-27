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
    roomFromUrl: function () {
      try { return (new URLSearchParams(location.search).get('room') || '').toUpperCase(); } catch (_) { return ''; }
    },
    inviteUrl: function (code) {
      var url = new URL(location.href);
      url.search = '';
      url.hash = '';
      url.searchParams.set('room', code);
      return url.toString();
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
        var room = bridge.roomFromUrl();
        if (saved && saved.code && saved.playerId && saved.name && room === saved.code.toUpperCase()) {
          bridge.sendToUnity({ kind: 'resuming', roomCode: saved.code });
          bridge.socket.emit('join_room', saved, function (data) {
            if (data && !data.ok && data.error === 'NOT_FOUND') bridge.clearSession();
            bridge.sendToUnity({ kind: 'ack', eventName: 'resume_room', data: data || {} });
          });
        }
      });
      bridge.socket.on('disconnect', function () { bridge.sendToUnity({ kind: 'disconnected' }); });
      bridge.socket.on('connect_error', function () {
        bridge.sendToUnity({ kind: 'bridgeError', message: 'Cannot reach the game server.' });
      });
      bridge.socket.on('state', function (state) { bridge.sendToUnity({ kind: 'state', state: state }); });
      bridge.socket.on('fx', function (fx) { bridge.sendToUnity({ kind: 'fx', fx: fx }); });
      bridge.socket.on('canvas', function (canvas) { bridge.sendToUnity({ kind: 'canvas', canvas: canvas }); });
      bridge.socket.on('draw', function (draw) { bridge.sendToUnity({ kind: 'draw', draw: draw }); });
      bridge.socket.on('canvases', function (all) { bridge.sendToUnity({ kind: 'canvases', all: all }); });
      bridge.socket.on('avatar_draw', function (draw) { bridge.sendToUnity({ kind: 'avatarDraw', draw: draw }); });
      bridge.socket.on('avatar', function (avatar) { bridge.sendToUnity({ kind: 'avatar', avatar: avatar }); });
      bridge.socket.on('avatars', function (avatars) { bridge.sendToUnity({ kind: 'avatars', avatars: avatars }); });
      bridge.sendToUnity({ kind: 'invite', roomCode: bridge.roomFromUrl() });
    },
    emit: function (eventName, payload, hasPayload, wantsAck) {
      var bridge = this;
      if (!bridge.socket || !bridge.socket.connected) {
        if (wantsAck) bridge.sendToUnity({ kind: 'ack', eventName: eventName, data: { ok: false, error: 'OFFLINE' } });
        return;
      }
      var done = function (data) {
        if (data && data.ok && (eventName === 'create_room' || eventName === 'join_room')) {
          bridge.writeSession({ code: data.code, playerId: data.playerId, name: payload.name });
        }
        if (eventName === 'leave_room' && data && data.ok) bridge.clearSession();
        if (eventName === 'join_room' && data && data.error === 'NOT_FOUND') bridge.clearSession();
        if (wantsAck) bridge.sendToUnity({ kind: 'ack', eventName: eventName, data: data || {} });
      };
      if (hasPayload) bridge.socket.emit(eventName, payload, done);
      else bridge.socket.emit(eventName, done);
    },
    copy: function (text, message) {
      var bridge = this;
      var fallback = function () {
        try {
          var area = document.createElement('textarea');
          area.value = text;
          area.setAttribute('readonly', '');
          area.style.position = 'fixed';
          area.style.opacity = '0';
          document.body.appendChild(area);
          area.select();
          var ok = document.execCommand('copy');
          document.body.removeChild(area);
          bridge.sendToUnity({ kind: 'shared', message: ok ? message : text });
        } catch (_) {
          bridge.sendToUnity({ kind: 'shared', message: text });
        }
      };
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(function () {
          bridge.sendToUnity({ kind: 'shared', message: message });
        }).catch(fallback);
      } else {
        fallback();
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
    SplitKeysBridge.emit('create_room', { name: UTF8ToString(namePtr) }, true, true);
  },
  SK_JoinRoom__deps: ['$SplitKeysBridge'],
  SK_JoinRoom: function (codePtr, namePtr) {
    SplitKeysBridge.emit('join_room', { code: UTF8ToString(codePtr), name: UTF8ToString(namePtr) }, true, true);
  },
  SK_AvatarDraw__deps: ['$SplitKeysBridge'],
  SK_AvatarDraw: function (jsonPtr) {
    var bridge = SplitKeysBridge;
    if (!bridge.socket || !bridge.socket.connected) return;
    try { bridge.socket.emit('avatar_draw', JSON.parse(UTF8ToString(jsonPtr))); } catch (_) {}
  },
  SK_AvatarSet__deps: ['$SplitKeysBridge'],
  SK_AvatarSet: function (jsonPtr) {
    var bridge = SplitKeysBridge;
    if (!bridge.socket || !bridge.socket.connected) return;
    try { bridge.socket.emit('avatar_set', JSON.parse(UTF8ToString(jsonPtr))); } catch (_) {}
  },
  SK_Guess__deps: ['$SplitKeysBridge'],
  SK_Guess: function (textPtr) {
    SplitKeysBridge.emit('guess', { text: UTF8ToString(textPtr) }, true, false);
  },
  SK_Draw__deps: ['$SplitKeysBridge'],
  SK_Draw: function (jsonPtr) {
    var bridge = SplitKeysBridge;
    if (!bridge.socket || !bridge.socket.connected) return;
    try { bridge.socket.emit('draw', JSON.parse(UTF8ToString(jsonPtr))); } catch (_) {}
  },
  SK_ChooseWord__deps: ['$SplitKeysBridge'],
  SK_ChooseWord: function (levelPtr) {
    SplitKeysBridge.emit('choose_word', { level: UTF8ToString(levelPtr) }, true, false);
  },
  SK_UpdateSettings__deps: ['$SplitKeysBridge'],
  SK_UpdateSettings: function (jsonPtr) {
    var bridge = SplitKeysBridge;
    if (!bridge.socket || !bridge.socket.connected) return;
    try { bridge.socket.emit('update_settings', JSON.parse(UTF8ToString(jsonPtr)), function () {}); } catch (_) {}
  },
  SK_SwitchTeam__deps: ['$SplitKeysBridge'],
  SK_SwitchTeam: function () {
    SplitKeysBridge.emit('switch_team', null, false, false);
  },
  SK_StartGame__deps: ['$SplitKeysBridge'],
  SK_StartGame: function () {
    SplitKeysBridge.emit('start_game', null, false, true);
  },
  SK_AddBot__deps: ['$SplitKeysBridge'],
  SK_AddBot: function () {
    SplitKeysBridge.emit('add_bot', null, false, false);
  },
  SK_RemoveBot__deps: ['$SplitKeysBridge'],
  SK_RemoveBot: function () {
    SplitKeysBridge.emit('remove_bot', null, false, false);
  },
  SK_LeaveRoom__deps: ['$SplitKeysBridge'],
  SK_LeaveRoom: function () {
    SplitKeysBridge.emit('leave_room', null, false, true);
  },
  SK_SetRoomQuery: function (codePtr) {
    var code = UTF8ToString(codePtr);
    var url = new URL(location.href);
    if (code) url.searchParams.set('room', code);
    else url.searchParams.delete('room');
    history.replaceState({}, '', url.toString());
    document.title = code ? 'Split Keys · ' + code : 'Split Keys';
  },
  SK_RequestInviteCode__deps: ['$SplitKeysBridge'],
  SK_RequestInviteCode: function () {
    SplitKeysBridge.sendToUnity({ kind: 'invite', roomCode: SplitKeysBridge.roomFromUrl() });
  },
  SK_InviteUrl__deps: ['$SplitKeysBridge'],
  SK_InviteUrl: function (codePtr) {
    var text = SplitKeysBridge.inviteUrl(UTF8ToString(codePtr));
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  },
  SK_Invite__deps: ['$SplitKeysBridge'],
  SK_Invite: function (codePtr) {
    var code = UTF8ToString(codePtr);
    var url = SplitKeysBridge.inviteUrl(code);
    var bridge = SplitKeysBridge;
    var touch = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
    if (navigator.share && touch) {
      navigator.share({ title: 'Split Keys', text: 'Join my Split Keys room: ' + code, url: url })
        .catch(function (error) { if (!error || error.name !== 'AbortError') bridge.copy(url, 'Link copied!'); });
    } else {
      bridge.copy(url, 'Link copied!');
    }
  },
  SK_CopyText__deps: ['$SplitKeysBridge'],
  SK_CopyText: function (textPtr, messagePtr) {
    SplitKeysBridge.copy(UTF8ToString(textPtr), UTF8ToString(messagePtr));
  },
  SK_Vibrate: function (ms) {
    try { if (navigator.vibrate) navigator.vibrate(ms); } catch (_) {}
  },
  SK_IsTouch: function () {
    try { return window.matchMedia && window.matchMedia('(pointer: coarse)').matches ? 1 : 0; } catch (_) { return 0; }
  },
  SK_ReducedMotion: function () {
    try { return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 1 : 0; } catch (_) { return 0; }
  },
  SK_NowMs: function () {
    return Date.now();
  }
});
