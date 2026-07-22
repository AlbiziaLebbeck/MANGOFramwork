mergeInto(LibraryManager.library, {
  MangosFetchWithCredentials: function (
    gameObjectNamePtr,
    callbackMethodPtr,
    requestIdPtr,
    urlPtr,
    methodPtr,
    jsonBodyPtr,
    timeoutMilliseconds
  ) {
    const gameObjectName = UTF8ToString(gameObjectNamePtr);
    const callbackMethod = UTF8ToString(callbackMethodPtr);
    const requestId = UTF8ToString(requestIdPtr);
    const url = UTF8ToString(urlPtr);
    const method = UTF8ToString(methodPtr);
    const jsonBody = UTF8ToString(jsonBodyPtr);

    window.mangosAuthRequests = window.mangosAuthRequests || {};

    const controller = new AbortController();
    const state = {
      controller: controller,
      timedOut: false
    };
    window.mangosAuthRequests[requestId] = state;

    const options = {
      method: method,
      credentials: "include",
      cache: "no-store",
      headers: {
        "Accept": "application/json"
      },
      signal: controller.signal
    };

    if (jsonBody) {
      options.headers["Content-Type"] = "application/json";
      options.body = jsonBody;
    }

    const timeoutId = setTimeout(function () {
      state.timedOut = true;
      controller.abort();
    }, Math.max(1000, timeoutMilliseconds));

    fetch(url, options)
      .then(function (response) {
        return response.text().then(function (body) {
          return {
            requestId: requestId,
            statusCode: response.status,
            body: body,
            error: "",
            errorKind: ""
          };
        });
      })
      .catch(function (error) {
        return {
          requestId: requestId,
          statusCode: 0,
          body: "",
          error: error && error.name ? error.name : "NetworkError",
          errorKind: state.timedOut ? "timeout" : "network"
        };
      })
      .then(function (result) {
        clearTimeout(timeoutId);
        delete window.mangosAuthRequests[requestId];
        if (window.unityInstance) {
          window.unityInstance.SendMessage(
            gameObjectName,
            callbackMethod,
            JSON.stringify(result)
          );
        }
      });
  },

  MangosCancelBrowserRequest: function (requestIdPtr) {
    const requestId = UTF8ToString(requestIdPtr);
    if (!window.mangosAuthRequests || !window.mangosAuthRequests[requestId]) {
      return;
    }

    window.mangosAuthRequests[requestId].controller.abort();
    delete window.mangosAuthRequests[requestId];
  },

  MangosRedirectToLogin: function (webBaseUrlPtr) {
    const webBaseUrl = UTF8ToString(webBaseUrlPtr);
    const loginUrl = new URL("/login", webBaseUrl);
    loginUrl.searchParams.set("redirect", window.location.href);
    window.location.assign(loginUrl.toString());
  }
});
