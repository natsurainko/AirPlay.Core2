namespace AirPlay.Core2.Models.Messages;

public record struct MediaProgressInfo(TimeSpan Duration, TimeSpan Position);