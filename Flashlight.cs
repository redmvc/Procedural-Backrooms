
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class Flashlight : UdonSharpBehaviour
{
    public new GameObject light;
    [UdonSynced] public bool isActive = true;
    private bool isActiveLocal = true;
    public AudioSource clickSound;

    void Start() {}

    public override void OnPickupUseDown ()
    {
        this.ToggleNetwork ();
    }

    private void ToggleNetwork ()
    {
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
}
