using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimeTrap.UI
{
    // Tells a slider's owner when the player has let go of it (the SFX slider plays a sample then). A change made with
    // the arrow keys counts as let go once the value has stood still for a moment.
    public class SliderRelease : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action onRelease;
        public float settleTime = 0.3f;

        bool held;
        float changedAt = -1f;

        void Start()
        {
            var slider = GetComponent<Slider>();
            if (slider != null) slider.onValueChanged.AddListener(v => changedAt = Time.unscaledTime);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            held = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            held = false;
            changedAt = -1f;
            onRelease?.Invoke();
        }

        void Update()
        {
            if (held || changedAt < 0f || Time.unscaledTime - changedAt < settleTime) return;
            changedAt = -1f;
            onRelease?.Invoke();
        }

        void OnDisable()
        {
            held = false;
            changedAt = -1f;
        }
    }
}
