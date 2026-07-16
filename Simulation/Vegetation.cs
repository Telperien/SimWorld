namespace Simulation;

public struct Vegetation
{
    public int X;
    public int Y;
    public byte Type;
    public byte Stage;

    // Stock partagé restant (récoltable). Initialisé au FoodValue du
    // type à la création ; atteint 0 => l'instance disparaît.
    public int FoodRemaining;

    // Tick absolu de mort par vieillesse. -1 = immortel par l'âge.
    public int DeathTick;
}
