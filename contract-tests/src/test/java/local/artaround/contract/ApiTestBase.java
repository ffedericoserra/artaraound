package local.artaround.contract;

import org.junit.jupiter.api.BeforeAll;
import io.restassured.RestAssured;

public abstract class ApiTestBase {
    @BeforeAll
    static void setup() {
        // Defaults keep tests runnable from an IDE, where surefire's properties are not set.
        RestAssured.baseURI = System.getProperty("api.baseUri", "http://localhost");
        RestAssured.port = Integer.getInteger("api.port", 8000);
    }
}
