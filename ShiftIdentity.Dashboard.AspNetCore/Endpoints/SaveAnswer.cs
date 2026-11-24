using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;

/// <summary>
/// The save of an identity endpoint, and its answer when the rows are committed although a step that runs after the
/// commit failed, such as an after-commit handler that the host registered on users. Nothing was undone, so the
/// endpoint answers as the framework's CRUD endpoints do: as for a successful save, with the same status and the same
/// body (generated passwords included), the failure as the message, and the <see cref="ShiftEntityResponse.SavedWithFailure"/>
/// marker. Every failure is logged.
/// </summary>
internal static class SaveAnswer
{
    /// <summary>Saves through the repository. Returns the failure after the commit, or null when there was none.</summary>
    public static async Task<AfterCommitException?> SaveAsync(UserRepository repository, HttpContext httpContext)
    {
        try
        {
            await repository.SaveChangesAsync();
            return null;
        }
        catch (AfterCommitException saved)
        {
            var logger = httpContext.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger("ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints");

            foreach (var failure in saved.Failures)
                logger?.LogError(failure, "Users were saved by {Path}, but a step that runs after the commit failed. The request is answered as saved, with the failure as its message.",
                    httpContext.Request.Path.Value);

            return saved;
        }
    }

    /// <summary>
    /// The answer of a successful save, with the failure after the commit as its message and the marker, when there was
    /// one. The failure's message is its own when it is a <c>ShiftEntityException</c>, which says what failed in words
    /// meant for the user, and a general one otherwise (<see cref="AfterCommitException.SavedMessage"/>).
    /// </summary>
    public static TResponse With<TResponse>(TResponse response, AfterCommitException? saved) where TResponse : ShiftEntityResponse
    {
        if (saved is null)
            return response;

        response.Message = saved.SavedMessage();

        response.Additional = new Dictionary<string, object>(response.Additional ?? [])
        {
            [ShiftEntityResponse.SavedWithFailure] = true,
        };

        return response;
    }
}
