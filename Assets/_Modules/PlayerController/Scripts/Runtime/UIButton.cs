using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace MANGOsFramework.Experiment
{
    public class UIButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler 
    {
        public UnityEvent<bool> ButtonStateOutputEvent;
        public UnityEvent ButtonClickOutputEvent;

        public void OnPointerDown(PointerEventData eventData)
        {
            ButtonInput(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ButtonInput(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if(ButtonClickOutputEvent != null)
            {
                ButtonClickOutputEvent.Invoke();
            }
        }

        private void ButtonInput(bool buttonState)
        {
            if(ButtonStateOutputEvent != null)
            {
                ButtonStateOutputEvent.Invoke(buttonState);
            }
        }
    }
}

