mergeInto(LibraryManager.library, {
  $MangosAvatarRequests: {},
  MangosAvatarDownload__deps: ['$MangosAvatarRequests', '$UTF8ToString', 'malloc', 'free'],
  MangosAvatarDownload: function (id, urlPointer, credentials, callback) {
    var url = UTF8ToString(urlPointer);
    var controller = new AbortController();
    var state = {controller: controller};
    MangosAvatarRequests[id] = state;
    function finish(bytes, status) {
      if (MangosAvatarRequests[id] !== state) return;
      delete MangosAvatarRequests[id];
      clearTimeout(state.timer);
      var pointer = 0;
      try {
        if (bytes && bytes.length) {
          pointer = _malloc(bytes.length);
          if (!pointer) { bytes = null; status = 507; }
          else HEAPU8.set(bytes, pointer);
        }
        {{{ makeDynCall('viiii', 'callback') }}}(id, pointer, bytes ? bytes.length : 0, status);
      } finally { if (pointer) _free(pointer); }
    }
    state.timer = setTimeout(function () { finish(null, 408); controller.abort(); }, 45000);
    fetch(url, {credentials: credentials ? 'include' : 'same-origin', cache: credentials ? 'no-store' : 'default', signal: controller.signal})
      .then(function (response) {
        if (!response.ok) { finish(null, response.status); return null; }
        return response.arrayBuffer();
      })
      .then(function (buffer) { if (buffer) finish(new Uint8Array(buffer), 200); })
      .catch(function () { finish(null, controller.signal.aborted ? 499 : 0); });
  },
  MangosCancelAvatarDownload__deps: ['$MangosAvatarRequests'],
  MangosCancelAvatarDownload: function (id) {
    var state = MangosAvatarRequests[id];
    if (!state) return;
    delete MangosAvatarRequests[id];
    clearTimeout(state.timer);
    state.controller.abort();
  }
});
