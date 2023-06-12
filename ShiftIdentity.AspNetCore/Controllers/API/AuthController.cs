using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Controllers.API;

[Route("api/[controller]")]
[ApiController]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly ShiftIdentityOptions options;

    public AuthController(
        ShiftIdentityOptions options)
    {
        this.options = options;
    }

    [HttpPost("Login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginDTO loginDto)
    {
        var hash= HashService.GenerateHash(options.UserPassword);

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(s => s.GetUserByUsernameAsync(options.UserData.Username.ToLower())).ReturnsAsync(
            new User()
            {
                Email = options.UserData.Emails?.FirstOrDefault()?.Email ?? "",
                Phone = options.UserData.Phones?.FirstOrDefault()?.Phone ?? "",
                FullName = options.UserData.FullName,
                Username = options.UserData.Username,
                IsActive = true,
                AccessTrees = options.AccessTrees.Select(x => new UserAccessTree
                {
                    AccessTree = new AccessTree
                    {
                        Tree = x
                    }
                }),
                PasswordHash = hash.PasswordHash,
                Salt = hash.Salt,
            }.CreateShiftEntity(null, options.UserData.ID));
        userRepo.Setup(x=> x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var scopeRepo= new Mock<IScopeRepository>();
        scopeRepo.Setup(x => x.GetAllScopesAsync()).ReturnsAsync(options.Scopes.Select(x => new ScopeDTO { Name = x }));

        //var tokenService = new TokenService(options.Configuration, scopeRepo.Object, null);
        //var authService = new AuthService(userRepo.Object, options.Configuration, tokenService, null);
        //var authController= new Dashboard.AspNetCore.Controllers.AuthController(authService, null, tokenService, null);

        //return await authController.Login(loginDto);
    }

    /// <summary>
    /// Code challenge must be hashed with SHA512 algorithm and then converted to Hex string
    /// </summary>
    /// <param name="generateAuthCodeDto"></param>
    /// <returns></returns>
    [HttpPost("AuthCode")]
    public async Task<IActionResult> GenerateAuthCode([FromBody] GenerateAuthCodeDTO generateAuthCodeDto)
    {
        var claimService = new Mock<IClaimService>();
        claimService.Setup(s => s.GetUser()).Returns(options.UserData);

        var appRepo = new Mock<IAppRepository>();
        appRepo.Setup(s => s.GetAppAsync(options.App.AppId.ToLower())).ReturnsAsync(new App
        {
            AppId = options.App.AppId,
            DisplayName = options.App.DisplayName,
            RedirectUri = options.App.RedirectUri,
            Scopes = options.Scopes.Select(x => new AppScope { Scope = new Scope { Name = x } })
        });

        //var authController = new Dashboard.AspNetCore.Controllers.AuthController(
        //    new AuthService(null, options.Configuration, null, new AuthCodeService(appRepo.Object, authCodeStoreService)),
        //    new AuthCodeService(appRepo.Object, authCodeStoreService),
        //    null,
        //    claimService.Object
        //    );

        //return await authController.GenerateAuthCode(generateAuthCodeDto);
    }

    [HttpPost("Refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshDTO dto)
    {
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(s => s.FindAsync(options.UserData.ID, It.IsAny<DateTime?>(), It.IsAny<bool>())).ReturnsAsync(
            new User()
            {
                Email = options.UserData.Emails?.FirstOrDefault()?.Email ?? "",
                Phone = options.UserData.Phones?.FirstOrDefault()?.Phone ?? "",
                FullName = options.UserData.FullName,
                Username = options.UserData.Username,
                IsActive = true,
                AccessTrees = options.AccessTrees.Select(x => new UserAccessTree
                {
                    AccessTree = new AccessTree
                    {
                        Tree = x
                    }
                })
            }.CreateShiftEntity(null, options.UserData.ID));

        var tokenService = new TokenService(options.Configuration, null, userRepo.Object);

        //var authController = new Dashboard.AspNetCore.Controllers.AuthController(null, null, tokenService, null);

        //return await authController.Refresh(dto);
    }

    /// <summary>
    /// Code verifier is checked against SHA512 hash algorithm
    /// </summary>
    /// <param name="dto"></param>
    /// <returns></returns>
    [HttpPost("TokenWithAppIdOnly")]
    [AllowAnonymous]
    public async Task<IActionResult> GenereateExternalTokenWithAppIdOnly([FromBody] GenerateExternalTokenWithAppIdOnlyDTO dto)
    {
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(s => s.FindAsync(options.UserData.ID, It.IsAny<DateTime?>(), It.IsAny<bool>())).ReturnsAsync(
            new User()
            {
                Email = options.UserData.Emails?.FirstOrDefault()?.Email ?? "",
                Phone = options.UserData.Phones?.FirstOrDefault()?.Phone ?? "",
                FullName = options.UserData.FullName,
                Username = options.UserData.Username,
                IsActive = true,
                AccessTrees = options.AccessTrees.Select(x => new UserAccessTree
                {
                    AccessTree = new AccessTree
                    {
                        Tree = x
                    }
                })
            }.CreateShiftEntity(null, options.UserData.ID));

        var appRepo = new Mock<IAppRepository>();
        appRepo.Setup(s => s.GetAppAsync(options.App.AppId.ToLower())).ReturnsAsync(new App
        {
            AppId = options.App.AppId.ToLower(),
            DisplayName = options.App.DisplayName,
            RedirectUri = options.App.RedirectUri,
            Scopes = options.Scopes.Select(x => new AppScope { Scope = new Scope { Name = x } })
        });

        //var authCodeService = new AuthCodeService(appRepo.Object, authCodeStoreService);
        //var tokenService = new TokenService(options.Configuration, null, null);
        //var authService = new AuthService(userRepo.Object, options.Configuration, tokenService, authCodeService);
        //var authController = new Dashboard.AspNetCore.Controllers.AuthController(authService, authCodeService, tokenService, null);

        //return await authController.GenereateExternalTokenWithAppIdOnly(dto);
    }
}
