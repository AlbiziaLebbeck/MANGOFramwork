(function(){
  window.MangoTtbShowLoadError=function(){
    var message=document.getElementById('ttb-load-message'),button=document.getElementById('ttb-enter');
    if(message)message.textContent='ยังเปิดแกลเลอรีไม่ได้ กรุณาตรวจการเชื่อมต่อแล้วลองอีกครั้ง';
    if(button){
      button.textContent='ลองเชื่อมต่ออีกครั้ง';button.disabled=false;
      if(!button.dataset.connectionRetry){button.dataset.connectionRetry='true';button.addEventListener('click',function(event){event.preventDefault();event.stopImmediatePropagation();if(!window.MangoCommerceIsBusy?.())location.reload()},true)}
    }
  };
  window.MangoTtbLoadUnity=function(loader){
    // The operating-system online hint can be wrong. A real request, never a background retry, decides whether the runtime is reachable.
    loader.addEventListener('error',window.MangoTtbShowLoadError);
    document.body.appendChild(loader);
  };
})();
