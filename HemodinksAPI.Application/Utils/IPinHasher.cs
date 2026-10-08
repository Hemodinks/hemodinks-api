namespace HemodinksAPI.Application.Utils;

public interface IPinHasher
{
    string HashPin(string pin);
    bool VerifyPin(string pin, string hash);
}
