namespace UPBazaar.Modules.Academy.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
/// </summary>
public static class AcademyPermissions
{
    public const string CoursesRead = "academy.courses.read";

    public const string CoursesWrite = "academy.courses.write";
}
