using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;

namespace DigitalBrain.Google;

public static class GoogleCredentialFactory
{
    public static UserCredential FromRefreshToken(string clientId, string clientSecret, string refreshToken, params string[] scopes)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = scopes
        });
        var token = new TokenResponse { RefreshToken = refreshToken };
        return new UserCredential(flow, "digitalbrain-user", token);
    }
}
