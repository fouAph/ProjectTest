using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    public KeyCode interactionKey = KeyCode.E;
    // Update is called once per frame

    Interactable focus;
    UIManager uIManager => UIManager.instance;

    void Update()
    {
        //ketika variabel focus tidak kosong
        if (focus)
        {
            //ketika menekan key yang ada pada variable key
            if (Input.GetKeyDown(interactionKey))
            {

                focus.OnInteract();
                // focus.isInteracting = !focus.isInteracting;
                uIManager.SetInteractionUI(!uIManager.isPopupUIOpen);
            }
        }
    }

    //ketika masuk ke dalam collider
    private void OnTriggerEnter(Collider other)
    {
        //inisialisasi vaariable interact menjadi gameobject
        var interact = other.gameObject;

        //ketika gameobject yang dimasuki memiliki layer bernama "Interactable"
        if (interact.layer == LayerMask.NameToLayer("Interactable"))
        {
            print("is in range");

            //maka variable focus menjadi gameobject yang saat ini kita sentuh collidernya
            focus = interact.GetComponent<Interactable>();

            uIManager.ShowInteractionUI(focus);
            uIManager.SetInteractionUI(true);

        }

    }

    //ketika keluar dari collider
    private void OnTriggerExit(Collider other)
    {
        //jika variable focus tidak kosong, maka panggil metode OnFinish yang ada pada variable focus
        if (focus) focus.OnFinish();

        //kemudian hilangkan isi dari variable focus
        focus = null;

        uIManager.SetInteractionUI(false);
        uIManager.isPopupUIOpen = false;

    }


}
