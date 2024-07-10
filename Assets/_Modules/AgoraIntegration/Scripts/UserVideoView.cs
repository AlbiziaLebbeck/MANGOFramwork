using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UserVideoView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image videoStateImage;
    [SerializeField] private Button fullscreenButton;
    [SerializeField] private UIFader fader;
    [SerializeField] private uint uid;

    private void OnEnable()
    {
        //ProjectOpenEvent
        //ProjectCloseEvent
    }
    private void OnDisable()
    {
        //ProjectOpenEvent
    }

    private void Start()
    {
        PersistentCanvas.ChatCanvas.SwitchSprite(videoStateImage.gameObject, ChatCanvas.SpriteType.VideoOff);
        fader?.FadeOut();
    }

    public void AssignUid(uint uid)
    {
        this.uid = uid;
    }

    private void OnProjectOpen(uint uid)
    {
        if (uid != this.uid) return;

        fullscreenButton.onClick.RemoveAllListeners();
        fullscreenButton.onClick.AddListener(delegate { OnClick_FullScreen(); });
    }
    private void OnProjectClose(uint uid)
    {
        if (uid != this.uid) return;

        fullscreenButton.onClick.RemoveAllListeners();
    }

    public void UpdateShareScreenStatus()
    {

    }


    private void OnClick_FullScreen()
    {
        //Set Video

        PersistentCanvas.ChatCanvas.SwitchSprite(videoStateImage.gameObject, ChatCanvas.SpriteType.FullScreenOff);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        fader?.FadeIn();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        fader?.FadeOut();
    }
}
