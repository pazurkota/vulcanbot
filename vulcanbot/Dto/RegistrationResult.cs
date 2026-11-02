namespace vulcanbot.Dto;

/// <param name="RestUrl">Base URL for data requests (from the registration response), e.g.
/// <c>https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/</c>.</param>
public record RegistrationResult(
    DeviceKey DeviceKeys,
    string Symbol,
    string RestUrl,
    int LoginId,
    string UserLogin,
    string UserName
    );
