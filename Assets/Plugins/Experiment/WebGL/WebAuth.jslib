mergeInto(LibraryManager.library, {
  OpenOAuthPopup: function (callbackObjectPtr, callbackMethodPtr, urlPtr, reUrlPtr) {
    const callbackObject = UTF8ToString(callbackObjectPtr);
    const callbackMethod = UTF8ToString(callbackMethodPtr);
    const authUrl = UTF8ToString(urlPtr);
    const redirUrl = UTF8ToString(reUrlPtr)

    const popup = window.open(authUrl, "Login", "width=500, height=600");

    let receivedAuthCode = false;

    const interval = setInterval(function (){
      try{
        if(popup.location.href.startsWith(`${redirUrl}/?authCode=`)){   
          const searchParams = new URL(popup.location.href).searchParams;
          const authCode = searchParams.get("authCode");

          if(authCode){
            window.unityInstance.SendMessage(callbackObject, callbackMethod, authCode);
            receivedAuthCode = true;
            popup.close();
            clearInterval(interval);
          }
        }
      } catch (e){
        // clearInterval(interval);
      }

      if(popup.closed){
        clearInterval(interval);

        if(!receivedAuthCode){
          window.unityInstance.SendMessage(callbackObject, callbackMethod, "ERROR_NO_AUTHCODE");
        }
      }
    }, 100);
  },

  SaveToLocalStorage: function(keyPtr, valuePtr){
    var key = UTF8ToString(keyPtr);
    var value = UTF8ToString(valuePtr);
    localStorage.setItem(key, value);
  }, 

  GetLocalStorage: function (keyPtr) {
    var key = UTF8ToString(keyPtr);
    var value = localStorage.getItem(key);
    if (!value) return 0;

    var buffer = _malloc(lengthBytesUTF8(value) + 1);
    stringToUTF8(value, buffer, lengthBytesUTF8(value) + 1);
    return buffer;
  },

  HasLoggedIn: function (){
    var token = localStorage.getItem("metaauth_accessToken");
    var expiredAt = localStorage.getItem("metaauth_accessTokenExpiresAt")
    var userId = localStorage.getItem("metaauth_userId");

    if(!token || !expiredAt || !userId) return 0;

    var now = Date.now();
    var expires = Date.parse(expiredAt);

    if(isNaN(expires) || expires < now){
      localStorage.removeItem("metaauth_accessToken");
      localStorage.removeItem("metaauth_accessTokenExpiresAt")
      localStorage.removeItem("metaauth_userId");
      return 0;
    }

    return 1;
  },

    Logout: function () {
    localStorage.removeItem("metaauth_accessToken");
    localStorage.removeItem("metaauth_accessTokenExpiresAt");
    localStorage.removeItem("metaauth_userId");
  }
});
