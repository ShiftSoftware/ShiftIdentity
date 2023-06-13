﻿using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Core.Repositories;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services
{
    public class AuthCodeService
    {
        private readonly IAppRepository appRepo;
        private readonly AuthCodeStoreService authCodeStoreService;

        public AuthCodeService(
            IAppRepository appRepo,
            AuthCodeStoreService authCodeStoreService)
        {
            this.appRepo = appRepo;
            this.authCodeStoreService = authCodeStoreService;
        }

        public async Task<AuthCodeModel?> GenerateCodeAsync(GenerateAuthCodeDTO authCodeDto, long userId, int expireInMinutes = 5)
        {
            authCodeStoreService.RemoveExpireCodes();

            //The reutrn-url must be relative,
            //that means no external return-url allowed
            if (IsAbsoluteUrl(authCodeDto.ReturnUrl!))
                return null;

            var app = await appRepo.GetAppAsync(authCodeDto.AppId);

            if (app is null)
                return null;

            var code = new AuthCodeModel
            {
                AppId = app.AppId,
                AppDisplayName = app.DisplayName,
                CodeChallenge = authCodeDto.CodeChallenge,
                Expire = DateTime.UtcNow.AddMinutes(expireInMinutes),
                UserID = userId,
                Code = Guid.NewGuid(),
                RedirectUri = app.RedirectUri
            };

            authCodeStoreService.AddCode(code);

            return code;
        }

        private bool IsAbsoluteUrl(string url)
        {
            Uri result;
            return Uri.TryCreate(url, UriKind.Absolute, out result);
        }

        public async Task<AuthCodeModel?> VerifyCodeByAppIdOnly(string appId, Guid code, string codeVerifier)
        {
            authCodeStoreService.RemoveExpireCodes();

            var authCode = authCodeStoreService.GetCode(code);

            if (authCode == null)
                return null;

            if (authCode.AppId.Trim().ToString() != appId)
                return null;

            var app = await appRepo.GetAppAsync(authCode.AppId);
            if (app is null)
                return null;

            if (app.AppSecret is not null)
                return null;

            var hash = HashService.SHA512GenerateHash(codeVerifier);
            if (hash.ToLower() != authCode.CodeChallenge.ToLower())
                return null;

            authCodeStoreService.RemoveCode(authCode);

            return authCode;
        }
    }
}
