using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimeTrap.UI
{
    // The highlight of one menu control: amber text, a slide to the right and a marker while it is hovered or has the
    // keyboard focus. Sits on the Selectable itself (a Button, a Slider or a MenuSelector).
    public class MenuEntry : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler
    {
        public TextMeshProUGUI label;
        // What slides (buttons only). Empty: nothing moves.
        public RectTransform slide;
        public Image marker;
        // Tinted with the label (a slider's handle, a selector's arrows).
        public Graphic accent;
        public Graphic[] accents;
        public Color normalColour = MenuKit.Text;
        public Color highlightColour = MenuKit.Amber;

        public bool Highlighted { get; private set; }
        public Selectable Control => control != null ? control : (control = GetComponent<Selectable>());
        public Button Button => Control as Button;

        Selectable control;
        float offset;

        public void SetText(string text)
        {
            if (label == null) return;
            label.text = text;
            PlaceMarker();
        }

        // Just left of the text, wherever its alignment puts it.
        public void PlaceMarker()
        {
            if (marker == null || label == null) return;
            float width = label.GetPreferredValues(label.text).x;
            float box = ((RectTransform)transform).sizeDelta.x;
            bool leftAligned = label.alignment == TextAlignmentOptions.Left || label.alignment == TextAlignmentOptions.MidlineLeft;
            float textLeft = leftAligned ? -box * 0.5f : -width * 0.5f;
            ((RectTransform)marker.transform).anchoredPosition = new Vector2(textLeft - marker.rectTransform.sizeDelta.x * 0.5f - 10f, 0f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (EventSystem.current == null || !Control.IsInteractable()) return;
            if (EventSystem.current.currentSelectedGameObject != gameObject) EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnSelect(BaseEventData eventData)
        {
            Highlighted = true;
            if (!MenuKit.Silent) MenuKit.Hover?.Invoke();
            Apply();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            Highlighted = false;
            Apply();
        }

        void OnDisable()
        {
            Highlighted = false;
            offset = 0f;
            Apply();
        }

        // Unscaled time: the menus run while the game is frozen.
        void Update()
        {
            float target = Highlighted ? MenuKit.SlideDistance : 0f;
            if (Mathf.Approximately(offset, target)) return;
            offset = Mathf.MoveTowards(offset, target, Time.unscaledDeltaTime * 90f);
            Apply();
        }

        void Apply()
        {
            Color colour = Highlighted ? highlightColour : normalColour;
            if (label != null) label.color = colour;
            if (accent != null) accent.color = colour;
            if (accents != null)
                foreach (var graphic in accents)
                    if (graphic != null) graphic.color = Highlighted ? highlightColour : MenuKit.Dim;
            if (marker != null) marker.enabled = Highlighted;
            if (slide != null) slide.anchoredPosition = new Vector2(offset, 0f);
        }
    }
}
