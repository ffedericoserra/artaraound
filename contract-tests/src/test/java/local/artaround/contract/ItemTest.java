package local.artaround.contract;

import java.util.List;
import java.util.Map;

import io.restassured.http.ContentType;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import static io.restassured.RestAssured.given;
import static local.artaround.contract.TestData.*;
import static org.hamcrest.Matchers.*;

/*
 * GET    /api/items/:id - Read one item (public, or private to creator/buyer)
 * POST   /api/items     - Create item (auth)
 * DELETE /api/items/:id - Delete item (creator only)
 */
class ItemTest extends ApiTestBase {
    private static final String ITEMS_API = "/api/items";

    private static String ownerToken;
    private static String otherToken;
    private static String contentId;
    private static String privateItemId;

    /* ---------- helpers ---------- */

    /** universalId of the first content of the first seeded museum. */
    private static String anyContentId() {
        String museumId = given().get("/api/museums").then().statusCode(200)
            .extract().path("museums[0]._id");
        return given().get("/api/museums/" + museumId + "/contents").then().statusCode(200)
            .extract().path("contents[0].universalId");
    }

    private static String createItem(String token, boolean isPublic) {
        return given()
            .auth().oauth2(token)
            .contentType(ContentType.JSON)
            .body(Map.of(
                "contentId", contentId,
                "targetAudience", "Contract test",
                "isPublic", isPublic,
                "descriptions", List.of(Map.of(
                    "tone", "easy",
                    "texts", List.of(Map.of(
                        "text", "Contract test description",
                        "lengthCategory", "30s"))))))
        .when()
            .post(ITEMS_API)
        .then()
            .statusCode(201)
            .extract().path("item._id");
    }

    private static void deleteItem(String token, String id) {
        given().auth().oauth2(token).delete(ITEMS_API + "/" + id).then().statusCode(200);
    }

    @BeforeAll
    static void createPrivateItem() {
        ownerToken = registerAndGetToken(uniqueUsername(12));
        otherToken = registerAndGetToken(uniqueUsername(12));
        contentId = anyContentId();
        privateItemId = createItem(ownerToken, false);
    }

    // Unlike users, items can be deleted through the API, so clean up.
    @AfterAll
    static void deletePrivateItem() {
        deleteItem(ownerToken, privateItemId);
    }

    /* ---------- ownership ---------- */

    @Test
    void ownerCanReadPrivateItem() {
        given()
            .auth().oauth2(ownerToken)
        .when()
            .get(ITEMS_API + "/" + privateItemId)
        .then()
            .statusCode(200)
            .body("item._id", equalTo(privateItemId))
            .body("item.isOwned", equalTo(true));
    }

    @Test
    void otherUserCannotReadPrivateItem() {
        given()
            .auth().oauth2(otherToken)
        .when()
            .get(ITEMS_API + "/" + privateItemId)
        .then()
            .statusCode(403)
            .body("item", nullValue());
    }

    @Test
    void anonymousCannotReadPrivateItem() {
        given()
        .when()
            .get(ITEMS_API + "/" + privateItemId)
        .then()
            .statusCode(403)
            .body("item", nullValue());
    }

    /* ---------- not found ---------- */

    // A deleted item's id is well-formed in whatever id format the backend uses
    // (Mongo ObjectId today, maybe UUID in Spring). A hard-coded id would not be.
    @Test
    void deletedItemIsNotFound() {
        String id = createItem(ownerToken, true);
        deleteItem(ownerToken, id);

        given()
        .when()
            .get(ITEMS_API + "/" + id)
        .then()
            .statusCode(404)
            .body("error", equalTo("Item not found"));
    }

    // Malformed in every id format. A client error, so 400, never 500.
    @Test
    void malformedIdIsBadRequest() {
        given()
        .when()
            .get(ITEMS_API + "/not-an-id")
        .then()
            .statusCode(400)
            .body("error", not(containsString("ObjectId")));
    }
}
