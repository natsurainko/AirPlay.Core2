using AirPlay.Core2.Models.Messages.Rtsp;

namespace AirPlay.Core2.Extensions;

public static class RtspRequestMessageExtensions
{
    public static RtspResponseMessage CreateResponse(this RtspRequestMessage rtspRequestMessage)
    {
        RtspResponseMessage responseMessage = new();
        responseMessage.Headers.Add(new RtspHeader("Server", Constants.AIRTUNES_SERVER_VERSION));

        if (rtspRequestMessage.Headers.TryGetValue("CSeq", out var cSeqHeader))
            responseMessage.Headers.Add(cSeqHeader);

        return responseMessage;
    }
}
