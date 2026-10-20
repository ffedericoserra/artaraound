package local.artaround.contract;

import java.util.Map;
import java.util.concurrent.ThreadLocalRandom;

import io.restassured.http.ContentType;
import io.restassured.response.Response;

import static io.restassured.RestAssured.given;

/** Test data and setup shared by all test classes. */
final class TestData {
    static final String REGISTER_API = "/api/auth/register";

    // Not a secret: throwaway accounts on a dev database. Meets the 6..100 rule.
    static final String TEST_PASSWORD = "12345678";

    private TestData() {}

    static String randomString(int length) {
        String chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        StringBuilder sb = new StringBuilder(length);
        for (int i = 0; i < length; i++) {
            sb.append(chars.charAt(ThreadLocalRandom.current().nextInt(chars.length())));
        }
        return sb.toString();
    }

    /** Fresh, valid username of exactly the given length (starts with "t_"). */
    static String uniqueUsername(int length) {
        return "t_" + randomString(length - 2);
    }

    static Response attemptRegister(String username, String password) {
        return given()
            .contentType(ContentType.JSON)
            .body(Map.of(
                "username", username,
                "password", password,
                "email", username + "@test.local"))
        .when()
            .post(REGISTER_API);
    }

    /** Registers a fresh user and returns its JWT (register already logs in). */
    static String registerAndGetToken(String username) {
        return attemptRegister(username, TEST_PASSWORD)
        .then()
            .statusCode(201)
            .extract().path("token");
    }
}
