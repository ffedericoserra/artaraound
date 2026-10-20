package local.artaround.contract;

import java.util.Map;
import java.util.stream.Stream;

import io.restassured.http.ContentType;
import io.restassured.response.Response;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

import static io.restassured.RestAssured.given;
import static local.artaround.contract.TestData.*;
import static org.hamcrest.MatcherAssert.assertThat;
import static org.hamcrest.Matchers.*;

/*
 * POST /api/auth/register - Create new user
 * POST /api/auth/login    - Login and get JWT
 * GET  /api/auth/me       - Get current user
 */
class AuthTest extends ApiTestBase {
    private static final String LOGIN_API = "/api/auth/login";
    private static final String ME_API = "/api/auth/me";

    // Validation user register limits
    private static final int USERNAME_MIN = 3;
    private static final int USERNAME_MAX = 30;
    private static final int PASSWORD_MIN = 6;
    private static final int PASSWORD_MAX = 100;

    private static final String TEST_USER = uniqueUsername(10);

    /* ---------- helpers ---------- */

    private static Response attemptLogin(String username, String password) {
        return given()
            .contentType(ContentType.JSON)
            .body(Map.of(
                "username", username,
                "password", password))
        .when()
            .post(LOGIN_API);
    }

    private static String loginAndGetToken() {
        return attemptLogin(TEST_USER, TEST_PASSWORD)
        .then()
            .statusCode(200)
            .extract().path("token");
    }

    private static String unknownUsername() {
        return "nobody_" + randomString(8);
    }

    @BeforeAll
    static void registerTestUser() {
        registerAndGetToken(TEST_USER);
    }

    /* ---------- argument sources ---------- */

    static Stream<String> invalidUsernames() {
        return Stream.of(
            randomString(USERNAME_MIN - 1),
            uniqueUsername(USERNAME_MAX + 1));
    }

    static Stream<String> validUsernames() {
        return Stream.of(
            uniqueUsername(USERNAME_MIN),
            uniqueUsername(USERNAME_MAX));
    }

    static Stream<String> invalidPasswords() {
        return Stream.of(
            randomString(PASSWORD_MIN - 1),
            randomString(PASSWORD_MAX + 1));
    }

    static Stream<String> validPasswords() {
        return Stream.of(
            randomString(PASSWORD_MIN),
            randomString(PASSWORD_MAX));
    }

    /* ---------- login ---------- */

    @Test
    void loginSuccess() {
        attemptLogin(TEST_USER, TEST_PASSWORD)
        .then()
            .statusCode(200);
    }

    @Test
    void jwtTokenPresent() {
        String token = loginAndGetToken();
        assertThat(token, not(emptyOrNullString()));
    }

    @Test
    void wrongPassword() {
        attemptLogin(TEST_USER, "wrong-password")
        .then()
            .statusCode(401)
            .body("token", nullValue());
    }

    @Test
    void unknownUser() {
        attemptLogin(unknownUsername(), TEST_PASSWORD)
        .then()
            .statusCode(401)
            .body("token", nullValue());
    }

    // Identical responses prevent user enumeration.
    @Test
    void wrongPasswordAndUnknownUserAreIndistinguishable() {
        Response wrongPass = attemptLogin(TEST_USER, "wrong-password");
        Response unknown = attemptLogin(unknownUsername(), TEST_PASSWORD);

        assertThat(unknown.statusCode(), equalTo(wrongPass.statusCode()));
        assertThat(unknown.jsonPath().getMap("$"), equalTo(wrongPass.jsonPath().getMap("$")));
    }

    /* ---------- /me ---------- */

    @Test
    void authMeWorksWithToken() {
        String token = loginAndGetToken();

        given()
            .auth().oauth2(token)
        .when()
            .get(ME_API)
        .then()
            .statusCode(200)
            .body("user.username", equalTo(TEST_USER));
    }

    @Test
    void authMeRejectsMissingToken() {
        given()
        .when()
            .get(ME_API)
        .then()
            .statusCode(401);
    }

    @Test
    void passwordHashCorrectlyMissing() {
        String token = loginAndGetToken();

        given()
            .auth().oauth2(token)
        .when()
            .get(ME_API)
        .then()
            .statusCode(200)
            .body("user", notNullValue())
            .body("user", not(hasKey("password")))
            .body("user", not(hasKey("password_hash")))
            .body("user", not(hasKey("passwordHash")));
    }

    /* ---------- register validation ---------- */

    // Rejected input never reaches the database, so any value of the right length works.
    // details[0].field proves the intended rule fired, not some other one.
    @ParameterizedTest
    @MethodSource("invalidUsernames")
    void registerRejectsInvalidUsername(String username) {
        attemptRegister(username, TEST_PASSWORD)
        .then()
            .statusCode(400)
            .body("error", equalTo("Validation Error"))
            .body("details[0].field", equalTo("username"));
    }

    // Short usernames run out quickly, so 409 (already taken) also proves the length passed
    // validation. This tests the validator, not registration.
    @ParameterizedTest
    @MethodSource("validUsernames")
    void registerAcceptsValidUsername(String username) {
        attemptRegister(username, TEST_PASSWORD)
        .then()
            .statusCode(anyOf(is(201), is(409)));
    }

    // "  ab  " must be rejected: users never see surrounding spaces. Spring's @Size does not
    // trim. Node quirk: Zod runs min(3) before trim(), so Mongoose rejects it instead, with a
    // different body ({ messages: [...] } instead of { details: [...] }). Only the common part
    // is asserted; Spring should return the usual details[] shape.
    @Test
    void registerRejectsUsernameTooShortAfterTrim() {
        given()
            .contentType(ContentType.JSON)
            .body(Map.of(
                "username", "  ab  ",
                "password", TEST_PASSWORD,
                "email", unknownUsername() + "@test.local"))
        .when()
            .post(REGISTER_API)
        .then()
            .statusCode(400)
            .body("error", equalTo("Validation Error"));
    }

    @ParameterizedTest
    @MethodSource("invalidPasswords")
    void registerRejectsInvalidPassword(String password) {
        attemptRegister(uniqueUsername(12), password)
        .then()
            .statusCode(400)
            .body("error", equalTo("Validation Error"))
            .body("details[0].field", equalTo("password"));
    }

    @ParameterizedTest
    @MethodSource("validPasswords")
    void registerAcceptsValidPassword(String password) {
        attemptRegister(uniqueUsername(12), password)
        .then()
            .statusCode(201);
    }
}
