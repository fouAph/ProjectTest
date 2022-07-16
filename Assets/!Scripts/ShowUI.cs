using UnityEngine;

public class ShowUI : Interactable
{
    public int uiNumber;

    public override void OnInteract()
    {
        base.OnInteract();
        if (!isMultipleInteraction)
            isInteracting = true;
        uIManager.ToogleUI(uiNumber);
    }

    public override void OnFinish()
    {
        base.OnFinish();
        if (!isMultipleInteraction)
            isInteracting = false;
        uIManager.HideUI();

    }




}
