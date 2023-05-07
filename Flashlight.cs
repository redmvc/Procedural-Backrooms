
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class Flashlight : UdonSharpBehaviour
{
    public new GameObject light;
    [UdonSynced] public bool isActive = true;
    private bool isActiveLocal = true;
    private bool isLocked = false; // If the flashlight is locked the player can't change it
    public AudioSource clickSound;

    void Start() {}

    public override void OnPickupUseDown ()
    {
        this.ToggleNetwork ();
    }

    private void ToggleNetwork ()
    {
        if (this.isLocked) return;

        this.isActive = !this.isActive;
        this.isActiveLocal = this.isActive;
        this.ToggleLight ();
    }

    public override void OnDeserialization ()
    {
        if (this.isActiveLocal != this.isActive) {
            this.isActiveLocal = this.isActive;
            this.ToggleLight ();
        }
    }

    private void ToggleLight (bool playSound = true)
    {
        this.light.SetActive (this.isActive);
        if (playSound) this.clickSound.Play ();
    }

    public void ForciblyTurnOff (bool lockFlashlight = true)
    {
        this.isLocked = lockFlashlight;
        ForciblyToggle (false);
    }

    public void ForciblyTurnOn (bool lockFlashlight = true)
    {
        this.isLocked = lockFlashlight;
        ForciblyToggle (true);
    }

    private void ForciblyToggle (bool activate)
    {
        if (this.isActive != activate) this.ToggleNetwork ();
    }

    public void Lock (bool lockFlashlight)
    {
        this.isLocked = lockFlashlight;
    }
}
