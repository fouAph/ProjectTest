using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    public GameObject popupUI;
    public GameObject[] uiGameobjects;

    public bool isPopupUIOpen;

    public TMP_Text interactionText;

    public static UIManager instance;

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogWarning("More than one UIManager Instance");
            DestroyImmediate(instance);
        }
        instance = this;
    }

    public void ToogleUI(int uiNumber)
    {
        popupUI = uiGameobjects[uiNumber];

        isPopupUIOpen = !isPopupUIOpen;
        popupUI.SetActive(isPopupUIOpen);
    }

    public void HideUI()
    {
        if (popupUI)
            popupUI.SetActive(false);
    }

    public void ShowInteractionUI(Interactable interactable)
    {
        interactionText.text = $"Press E to {interactable.interactionPrefix} {interactable.interactionName}";
    }

    public void SetInteractionUI(bool onOff)
    {
        interactionText.gameObject.SetActive(onOff);
    }

}
