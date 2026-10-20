mergeInto(LibraryManager.library, {
  MangoTtbEmit: function (pointer) {
    var payload = UTF8ToString(pointer);
    if (window.MangoTtb && typeof window.MangoTtb.receive === 'function') window.MangoTtb.receive(payload);
  },
  MangoTtbLogin: function () {
    var login = new URL('/login', 'https://mangosgo.com');
    login.searchParams.set('redirect', window.location.href);
    window.location.assign(login.toString());
  }
});
