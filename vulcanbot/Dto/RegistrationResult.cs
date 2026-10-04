namespace vulcanbot.Dto;

public record RegistrationResult(
    DeviceKey DeviceKeys,
    string Symbol,
    string EndpointUrl
    );