using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Interactable : MonoBehaviour
{
    public string interactionPrefix;
    public string interactionName;

    public bool isMultipleInteraction;
    public bool isInteracting;
    public UIManager uIManager => UIManager.instance;
    private void Awake()
    {
        gameObject.layer = LayerMask.NameToLayer("Interactable");
    }

    public virtual void OnInteract()
    {
        print("Interacted");
        isInteracting = !isInteracting;
        // uIManager.ToogleUI();
    }

    public virtual void OnFinish()
    {
        print("Finish Interact");

    }
}
