using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimeTrap.UI
{
    // One value out of a short list, shown between two arrows. Left / right change it; so does Enter, or a click on
    // either half.
    public class MenuSelector : Selectable, IPointerClickHandler, ISubmitHandler
    {
        public TextMeshProUGUI valueLabel;
        public Image leftArrow;
        public Image rightArrow;
        public Action<int> onChanged;
        public bool wrap = true;

        readonly List<string> options = new List<string>();

        public int Index { get; private set; }
        public int Count => options.Count;
        public string Value => Index >= 0 && Index < options.Count ? options[Index] : "";

        public void SetOptions(IList<string> list, int index)
        {
            options.Clear();
            if (list != null) options.AddRange(list);
            SetIndex(index, false);
        }

        public void SetIndex(int index, bool notify)
        {
            if (options.Count == 0)
            {
                Index = 0;
                if (valueLabel != null) valueLabel.text = "";
                return;
            }
            index = wrap ? (index % options.Count + options.Count) % options.Count : Mathf.Clamp(index, 0, options.Count - 1);
            bool changed = index != Index;
            Index = index;
            if (valueLabel != null) valueLabel.text = options[Index];
            if (changed && notify)
            {
                MenuKit.Click?.Invoke();
                onChanged?.Invoke(Index);
            }
        }

        public void Step(int by)
        {
            if (IsInteractable()) SetIndex(Index + by, true);
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left) Step(-1);
            else if (eventData.moveDir == MoveDirection.Right) Step(1);
            else base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            Step(1);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, eventData.position, eventData.pressEventCamera, out Vector2 local);
            Step(local.x < 0f ? -1 : 1);
        }
    }
}
