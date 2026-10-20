package local.artaround.contract;

import org.junit.jupiter.api.Test;

import static io.restassured.RestAssured.given;
import static org.hamcrest.Matchers.equalTo;

class HealthTest extends ApiTestBase {
    @Test
    void healthEndpointReturnsOk() {
        given()
        .when()
            .get("/api/health")
        .then()
            .statusCode(200)
            .body("status", equalTo("ok"));
    }
}