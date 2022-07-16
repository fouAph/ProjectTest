﻿using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MultiplayerARPG
{
    public class UICombatText : MonoBehaviour
    {
        public float lifeTime = 2f;
        public string format = "{0}";
        public bool showPositiveSign;
        public TextWrapper textComponent;

        private int amount;
        public int Amount
        {
            get { return amount; }
            set
            {
                amount = value;
                textComponent.text = string.Format(format, (showPositiveSign && amount > 0 ? "+" : string.Empty) + amount.ToString("N0"));
            }
        }

        private void Awake()
        {
            CacheComponents();
            Destroy(gameObject, lifeTime);
        }

        private void CacheComponents()
        {
            if (textComponent == null)
            {
                // Try get component which attached to this game object if `textComponent` was not set.
                textComponent = gameObject.GetOrAddComponent<TextWrapper>((comp) =>
                {
                    comp.unityText = GetComponent<Text>();
                    comp.textMeshText = GetComponent<TextMeshProUGUI>();
                });
            }
        }
    }
}
