﻿using System.Text;
using System.Security.Claims;
using OpenLibrary.Domain.Roles;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using OpenLibrary.Service.Interfaces;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using OpenLibrary.Service.DTOs.Students;
using Microsoft.Extensions.Configuration;

namespace OpenLibrary.Service.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<JwtTokenService> _logger;
    public JwtTokenService(IConfiguration configuration,
                           ILogger<JwtTokenService> logger)
    {
        _logger = logger;
        _configuration = configuration;
    }
    public Task<(string refreshToken, DateTime tokenValidityTime)> GenerateRefreshTokenAsync()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);

        if (!double.TryParse(_configuration["JWT:RefreshTokenValidityHours"], out double refreshTokenValidityHours))
            refreshTokenValidityHours = 5;

        var tokenExpiryTime = DateTime.UtcNow.AddHours(refreshTokenValidityHours);
        return Task.FromResult((Convert.ToBase64String(randomNumber), tokenExpiryTime));
    }

    public Task<(string token, DateTime tokenExpiryTime)> GenerateTokenAsync(StudentForResultDto user)
    {

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenKey = Encoding.UTF8.GetBytes(_configuration["JWT:Key"]!);
        var expireDate = DateTime.UtcNow.AddMinutes(double.Parse(_configuration["JWT:AccessTokenExpireMinutes"]!));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new Claim[]
            {
             new Claim("Id", user.Id.ToString()),
             new Claim("Role",user.Role.ToString()),
             new Claim("Email", user.Email),
             new Claim("Name", user.FirstName + " " + user.LastName),
             new Claim("Phone", user.Phone),
            }),
            Audience = _configuration["JWT:Audience"],
            Issuer = _configuration["JWT:Issuer"],
            IssuedAt = DateTime.UtcNow,
            Expires = expireDate,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(tokenKey), SecurityAlgorithms.HmacSha256Signature)
        };


        var token = tokenHandler.CreateToken(tokenDescriptor);
        return Task.FromResult((tokenHandler.WriteToken(token), expireDate));
    }

    public Task<StudentForResultDto?> GetUserByAccessTokenAsync(string accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
            return Task.FromResult<StudentForResultDto?>(null);

        var tokenHandler = new JwtSecurityTokenHandler();
        string secretKey = _configuration["JWT:Key"] ?? throw new ArgumentNullException("Key");
        var key = Encoding.ASCII.GetBytes(secretKey);
        try
        {
            tokenHandler.ValidateToken(accessToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                //ValidateIssuer = true,
                //ValidIssuer = _configuration["Jwt:ValidIssuer"],
                ValidateAudience = false,
                //ValidateAudience = true,
                //ValidAudience = _configuration["Jwt:ValidAudience"],
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);
            var jwtToken = (JwtSecurityToken)validatedToken;
            //Enum.TryParse(jwtToken.Claims.First(x => x.Type == "Role").Value, true, out Role role);
            Role role = (Role)Enum.Parse(typeof(Role), jwtToken.Claims.First(x => x.Type == "Role").Value, true);
            var user = new StudentForResultDto
            {
                Id = long.Parse(jwtToken.Claims.First(x => x.Type == "Id").Value),
                FirstName = jwtToken.Claims.First(x => x.Type == "Name").Value.Split().First(),
                LastName = jwtToken.Claims.First(x => x.Type == "Name").Value.Split().Last(),
                Role = role,
                Phone = jwtToken.Claims.First(x => x.Type == "Phone").Value,
                Email = jwtToken.Claims.First(x => x.Type == "Email").Value,
                IsVerified = true,
            };
            return Task.FromResult<StudentForResultDto?>(user);
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogInformation("Token has expired");
            throw;
            //return Task.FromResult<User?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error when validate token");
            return Task.FromResult<StudentForResultDto?>(null);
        }
    }

}
