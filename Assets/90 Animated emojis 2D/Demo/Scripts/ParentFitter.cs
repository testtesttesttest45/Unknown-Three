using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Smiles
{
    [ExecuteAlways]
    public class ParentFitter : UIBehaviour
    {
        private void LateUpdate()
        {
            OnRectTransformDimensionsChange();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            var rectTransform = transform as RectTransform;
            if (rectTransform == null)
                return;
            
            var parent = rectTransform.parent as RectTransform;
            if (parent == null)
                return;

            var currentRect = rectTransform.rect;
            var parentRect = parent.rect;

            if (currentRect.width == 0.0f || currentRect.height == 0.0f)
                return;

            var scale = new Vector3(parentRect.width / currentRect.width, parentRect.height / currentRect.height, 1.0f);
            if (rectTransform.localScale == scale)
                return;

            rectTransform.localScale = scale;
        }
    }
}